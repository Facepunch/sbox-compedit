using System;
using System.IO;
using System.Text.Json.Serialization;
using Editor.CodeEditors;
using Microsoft.CodeAnalysis;
using Sandbox.Internal;
using static Sandbox.PhysicsContact;

namespace Sandbox;

#nullable enable

partial class ComponentDefinition
{
	public static int BuildNumber { get; private set; } = 100;

	private Type? _type;
	private int _lastBuiltHash;

	[Hide, JsonIgnore] public Type? GeneratedType => GlobalGameNamespace.TypeLibrary.GetType( ResourcePath )?.TargetType;

	protected override void PostLoad()
	{
		UpdateMembers();

		if ( _type is null || _lastBuiltHash != GetDefinitionHash() )
		{
			Build();
		}
	}

	protected override void PostReload()
	{
		UpdateMembers();

		if ( _type is null || _lastBuiltHash != GetDefinitionHash() )
		{
			Build();
		}
	}

	/// <summary>
	/// A hash of this type's definition. If it's changed we need to rebuild.
	/// </summary>
	internal int GetDefinitionHash()
	{
		var hashCode = new HashCode();

		foreach ( var propertyDef in Properties )
		{
			hashCode.Add( propertyDef.GetDefinitionHash() );
		}

		foreach ( var method in Methods)
		{
			hashCode.Add( method.GetDefinitionHash() );
		}

		foreach ( var evnt in Events )
		{
			hashCode.Add( evnt.GetDefinitionHash() );
		}

		return hashCode.ToHashCode();
	}

	public string ClassName => $"Component_{ResourceId:x8}";

	private static string TypeRef<T>()
	{
		return TypeRef( typeof(T) );
	}

	private static string TypeRef( Type type )
	{
		if ( type.IsArray || type.IsGenericType || type.IsByRef || type.IsPointer || type.IsNested )
		{
			throw new NotImplementedException();
		}

		if ( type.Namespace is null )
		{
			return $"global::{type.Name}";
		}

		return $"global::{type.Namespace}.{type.Name}";
	}

	private static string StringLiteral( string value )
	{
		return $"@\"{value.Replace( "\"", "\"\"" )}\"";
	}

	public void Build()
	{
		var project = Project.Current;

		var outputPath = Path.Combine( project.GetCodePath(), "Generated", $"{ResourcePath}.cs" );
		var outputDir = Path.GetDirectoryName( outputPath );

		if ( !Directory.Exists( outputDir ) )
		{
			Directory.CreateDirectory( outputDir );
		}

		var ns = "Sandbox.Generated";

		if ( project.Config.TryGetMeta( "Compiler", out Compiler.Configuration compilerConfig ) && !string.IsNullOrEmpty( compilerConfig.RootNamespace ) )
		{
			ns = $"{compilerConfig.RootNamespace}.Generated";
		}

		using var writer = new StreamWriter( File.Create( outputPath ) );

		writer.WriteLine( $"// GENERATED FROM \"{ResourcePath}\"" );
		writer.WriteLine( "// DO NOT EDIT!" );
		writer.WriteLine();

		writer.WriteLine($"namespace {ns};");
		writer.WriteLine();

		WriteAttributes( writer );

		writer.WriteLine($"public sealed class {ClassName} : {TypeRef<Component>()}");
		writer.WriteLine("{");

		foreach ( var propertyDef in Properties )
		{
			WriteProperty( writer, propertyDef );
			writer.WriteLine();
		}

		writer.WriteLine( "}" );
		writer.WriteLine();
	}

	private void WriteAttributes( TextWriter writer )
	{
		writer.WriteLine( $"[{TypeRef<ClassNameAttribute>()}( {StringLiteral( ResourcePath )} )]" );
		writer.WriteLine( $"[{TypeRef<SourceLocationAttribute>()}( {StringLiteral( ResourcePath )}, 0 )]" );

		WriteDisplayAttributes( writer, Display, "" );
	}

	private static void WriteDisplayAttributes( TextWriter writer, DisplayInfo display, string indent = "    " )
	{
		if ( !string.IsNullOrEmpty( display.Name ) )
		{
			writer.WriteLine( $"{indent}[{TypeRef<TitleAttribute>()}( {StringLiteral( display.Name )} )]" );
		}

		if ( !string.IsNullOrEmpty( display.Description ) )
		{
			writer.WriteLine( $"{indent}[{TypeRef<DescriptionAttribute>()}( {StringLiteral( display.Description )} )]" );
		}

		if ( !string.IsNullOrEmpty( display.Group ) )
		{
			writer.WriteLine( $"{indent}[{TypeRef<GroupAttribute>()}( {StringLiteral( display.Group )} )]" );
		}

		if ( !string.IsNullOrEmpty( display.Icon ) )
		{
			writer.WriteLine( $"{indent}[{TypeRef<IconAttribute>()}( {StringLiteral( display.Icon )} )]" );
		}
	}

	private void WriteProperty( TextWriter writer, ComponentPropertyDefinition propertyDef )
	{
		writer.WriteLine( $"    [{TypeRef<PropertyAttribute>()}]" );

		WriteDisplayAttributes( writer, propertyDef.Display );

		switch ( propertyDef.Access )
		{
			case PropertyAccess.Public or PropertyAccess.PublicGet:
				writer.Write( "    public " );
				break;
			case PropertyAccess.Private:
				writer.Write( "    private " );
				break;
		}

		writer.Write( $"{TypeRef( propertyDef.Type )} {propertyDef.Name} {{ " );

		switch ( propertyDef.Access )
		{
			case PropertyAccess.PublicGet:
				writer.Write( "get; private set;" );
				break;
			default:
				writer.Write( "get; set;" );
				break;
		}

		writer.WriteLine( " }" );
	}
}

partial class ComponentPropertyDefinition
{
	internal int GetDefinitionHash()
	{
		var hashCode = new HashCode();

		hashCode.Add( Name );
		hashCode.Add( Access );
		hashCode.Add( Type.FullName );

		return hashCode.ToHashCode();
	}
}

partial class ComponentMethodDefinition
{
	internal int GetDefinitionHash()
	{
		var hashCode = new HashCode();

		hashCode.Add( Name );
		hashCode.Add( Override );
		hashCode.Add( Access );

		if ( !Override )
		{
			var binding = GetBinding( true );

			foreach ( var inputDef in binding.Inputs )
			{
				if ( inputDef.IsTarget )
				{
					continue;
				}

				hashCode.Add( inputDef.Name );
				hashCode.Add( inputDef.Type.FullName );
			}

			foreach ( var outputDef in binding.Outputs )
			{
				hashCode.Add( outputDef.Name );
				hashCode.Add( outputDef.Type.FullName );
			}
		}

		return hashCode.ToHashCode();
	}
}

partial class ComponentEventDefinition
{
	internal int GetDefinitionHash()
	{
		var hashCode = new HashCode();

		hashCode.Add( Name );

		foreach ( var inputDef in Inputs )
		{
			if ( inputDef.IsTarget )
			{
				continue;
			}

			hashCode.Add( inputDef.Name );
			hashCode.Add( inputDef.Type.FullName );
		}

		return hashCode.ToHashCode();
	}
}
