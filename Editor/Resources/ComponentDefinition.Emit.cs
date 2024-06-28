using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Editor;
using Facepunch.ActionGraphs;
using Sandbox.Diagnostics;
using Sandbox.Internal;

namespace Sandbox;

#nullable enable

partial class ComponentDefinition
{
	[JsonIgnore]
	public int BuildNumber { get; private set; } = 100;

	private Type? _type;
	private int _lastBuiltHash;

	[Hide, JsonIgnore] public Type? GeneratedType => _type ??= GlobalGameNamespace.TypeLibrary.GetType( ResourcePath )?.TargetType;

	protected override void PostLoad()
	{
		UpdateMembers();

		if ( GeneratedType is null || _lastBuiltHash != GetDefinitionHash() )
		{
			Build();
		}
	}

	protected override void PostReload()
	{
		UpdateMembers();

		if ( GeneratedType is null || _lastBuiltHash != GetDefinitionHash() )
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

		if ( type == typeof(void) )
		{
			return "void";
		}

		if ( type.Namespace is null )
		{
			return $"global::{type.Name}";
		}

		return $"global::{type.Namespace}.{type.Name}";
	}

	private static string StringLiteral( string? value, string indentation = "    " )
	{
		if ( value is null ) return "null";

		if ( value.Contains( '\n' ) && !value.Contains( "\"\"\"" ) )
		{
			return $"{Environment.NewLine}{indentation}\"\"\"{Environment.NewLine}{indentation}{value.Replace( "\n", $"\n{indentation}" )}{Environment.NewLine}{indentation}\"\"\"";
		}

		if ( value.All( x => x >= 32 && x <= 126 ) )
		{
			value = value
				.Replace( "\\", "\\\\" )
				.Replace( "\"", "\\\"" );

			return $"\"{value}\"";
		}

		return $"@\"{value.Replace( "\"", "\"\"" )}\"";
	}

	private static JsonSerializerOptions JsonStringLiteralOptions { get; } = new JsonSerializerOptions { WriteIndented = true };

	private static string StringLiteral( JsonNode? node, string indentation = "    " )
	{
		return StringLiteral( node?.ToJsonString( JsonStringLiteralOptions ), indentation );
	}

	private static string Constant( object? value )
	{
		switch ( value )
		{
			case null:
				return "null";

			case string str:
				return StringLiteral( str );

			case int i:
				return i.ToString();

			case float f:
				return $"{f:R}f";

			case bool b:
				return b ? "true" : "false";

			case Resource res:
				return $"{TypeRef( typeof(ResourceLibrary) )}.{nameof(ResourceLibrary.Get)}<{TypeRef( value.GetType() )}>( {StringLiteral( res.ResourcePath )} )";

			default:
				return $"{TypeRef( typeof(Json) )}.{nameof(Json.Deserialize)}<{TypeRef( value.GetType() )}>( {StringLiteral( Json.ToNode( value ) )} )";
		}
	}

	public void Build()
	{
		_lastBuiltHash = GetDefinitionHash();

		++BuildNumber;

		var project = Project.Current;

		var outputPath = Path.Combine( project.GetCodePath(), "Generated", $"{ResourcePath}.cs" );
		var tempPath = $"{outputPath}.tmp";
		var outputDir = Path.GetDirectoryName( outputPath )!;

		if ( !Directory.Exists( outputDir ) )
		{
			Directory.CreateDirectory( outputDir );
		}

		var ns = "Sandbox.Generated";

		if ( project.Config.TryGetMeta( "Compiler", out Compiler.Configuration compilerConfig ) && !string.IsNullOrEmpty( compilerConfig.RootNamespace ) )
		{
			ns = $"{compilerConfig.RootNamespace}.Generated";
		}

		var stubOnly = GeneratedType is null;

		using ( var writer = new StreamWriter( File.Create( tempPath ) ) )
		{
			writer.WriteLine( $"// GENERATED FROM \"{ResourcePath}\"" );
			writer.WriteLine( "// DO NOT EDIT!" );
			writer.WriteLine();

			writer.WriteLine( $"namespace {ns};" );
			writer.WriteLine();

			WriteAttributes( writer );

			writer.WriteLine( $"public sealed class {ClassName} : {TypeRef<Component>()}" );
			writer.WriteLine( "{" );

			if ( !stubOnly )
			{
				writer.WriteLine();
				writer.WriteLine( "    #region Properties" );
				writer.WriteLine();

				foreach ( var propertyDef in Properties )
				{
					WriteProperty( writer, propertyDef );
					writer.WriteLine();
				}

				writer.WriteLine( "    #endregion Properties" );
				writer.WriteLine();
				writer.WriteLine( "    #region Methods" );
				writer.WriteLine();

				foreach ( var methodDef in Methods )
				{
					WriteMethod( writer, methodDef );
					writer.WriteLine();
				}

				writer.WriteLine( "    #endregion Methods" );
				writer.WriteLine();
				writer.WriteLine( "    #region Events" );
				writer.WriteLine();

				foreach ( var eventDef in Events )
				{
					WriteEvent( writer, eventDef );
					writer.WriteLine();
				}

				writer.WriteLine( "    #endregion Events" );
				writer.WriteLine();
			}

			writer.WriteLine( "}" );
			writer.WriteLine();
		}

		File.Move( tempPath, outputPath, true );

		if ( stubOnly )
		{
			_ = FullBuildAfterCompile();
		}
	}

	private async Task FullBuildAfterCompile()
	{
		// TODO: clean this up!!

		await Task.Delay( 500 );
		await EditorUtility.Projects.WaitForCompiles();

		if ( GeneratedType is not null )
		{
			Build();
		}
	}

	private void WriteAttributes( TextWriter writer )
	{
		writer.WriteLine( $"[{TypeRef<ClassNameAttribute>()}( {StringLiteral( ResourcePath )} )]" );
		writer.WriteLine( $"[{TypeRef<SourceLocationAttribute>()}( {StringLiteral( ResourcePath )}, 0 )]" );

		WriteDisplayAttributes( writer, Display, "" );
	}

	private static void WriteDisplayAttributes( TextWriter writer, DisplayInfo display, string indent = "    ", string? target = null )
	{
		if ( target is not null )
		{
			target = $"{target}: ";
		}

		if ( !string.IsNullOrEmpty( display.Name ) )
		{
			writer.WriteLine( $"{indent}[{target}{TypeRef<TitleAttribute>()}( {StringLiteral( display.Name )} )]" );
		}

		if ( !string.IsNullOrEmpty( display.Description ) )
		{
			writer.WriteLine( $"{indent}[{target}{TypeRef<DescriptionAttribute>()}( {StringLiteral( display.Description )} )]" );
		}

		if ( !string.IsNullOrEmpty( display.Group ) )
		{
			writer.WriteLine( $"{indent}[{target}{TypeRef<GroupAttribute>()}( {StringLiteral( display.Group )} )]" );
		}

		if ( !string.IsNullOrEmpty( display.Icon ) )
		{
			writer.WriteLine( $"{indent}[{target}{TypeRef<IconAttribute>()}( {StringLiteral( display.Icon )} )]" );
		}
	}

	private static void WriteDisplayAttributes( TextWriter writer, Facepunch.ActionGraphs.DisplayInfo display, string indent = "    ", string? target = null )
	{
		if ( target is not null )
		{
			target = $"{target}: ";
		}

		if ( !string.IsNullOrEmpty( display.Title ) )
		{
			writer.WriteLine( $"{indent}[{target}{TypeRef<TitleAttribute>()}( {StringLiteral( display.Title )} )]" );
		}

		if ( !string.IsNullOrEmpty( display.Description ) )
		{
			writer.WriteLine( $"{indent}[{target}{TypeRef<DescriptionAttribute>()}( {StringLiteral( display.Description )} )]" );
		}

		if ( !string.IsNullOrEmpty( display.Group ) )
		{
			writer.WriteLine( $"{indent}[{target}{TypeRef<GroupAttribute>()}( {StringLiteral( display.Group )} )]" );
		}

		if ( !string.IsNullOrEmpty( display.Icon ) )
		{
			writer.WriteLine( $"{indent}[{target}{TypeRef<IconAttribute>()}( {StringLiteral( display.Icon )} )]" );
		}
	}

	private void WriteProperty( TextWriter writer, ComponentPropertyDefinition propertyDef )
	{
		writer.WriteLine( $"    #region {propertyDef.Display.Name}" );
		writer.WriteLine();
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

		writer.Write( " }" );

		if ( propertyDef.DefaultValue is null )
		{
			writer.WriteLine();
			writer.WriteLine( $"    #endregion {propertyDef.Display.Name}" );
			return;
		}

		writer.WriteLine( $" = {Constant( propertyDef.DefaultValue )};" );
		writer.WriteLine();
		writer.WriteLine( $"    #endregion {propertyDef.Display.Name}" );
	}

	private void WriteMethod( TextWriter writer, ComponentMethodDefinition methodDef )
	{
		writer.WriteLine( $"    #region {methodDef.Display.Name}" );
		writer.WriteLine();

		var baseMethod = methodDef.Override
			? typeof( Component ).GetMethod( methodDef.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance )
			: null;

		var binding = baseMethod is not null
			? NodeBinding.FromMethodBase( baseMethod, EditorNodeLibrary )
			: methodDef.GetBinding();

		var delegateParameters = new List<string>();
		var methodParameters = new List<string>();
		var arguments = new List<string>();

		foreach ( var inputDef in binding.Inputs.Where( x => x is { IsSignal: false } ) )
		{
			var parameter = $"{TypeRef( inputDef.Type )} {inputDef.Name}";

			if ( inputDef.IsTarget )
			{
				delegateParameters.Add( $"[{TypeRef<ActionGraphTargetAttribute>()}] {parameter}" );

				arguments.Add( "this" );
			}
			else
			{
				delegateParameters.Add( parameter );
				methodParameters.Add( parameter );

				arguments.Add( inputDef.Name );
			}
		}

		foreach ( var outputDef in binding.Outputs.Where( x => x is { IsSignal: false } ) )
		{
			var parameter = $"out {TypeRef( outputDef.Type )} {outputDef.Name}";

			delegateParameters.Add( parameter );
			methodParameters.Add( parameter );

			arguments.Add( $"out {outputDef.Name}" );
		}

		var delegateTypeName = $"{methodDef.Name}_Delegate";
		var delegateFieldName = $"{methodDef.Name}_Body";

		writer.WriteLine( $"    private delegate {TypeRef( typeof(void) )} {delegateTypeName}( {string.Join( ", ", delegateParameters )} );" );
		writer.WriteLine( $"    [{TypeRef<SkipHotloadAttribute>()}]" );
		writer.WriteLine( $"    private static {delegateTypeName} {delegateFieldName};" );
		writer.WriteLine();
		writer.WriteLine( $"    [{TypeRef<SourceLocationAttribute>()}( {StringLiteral( ResourcePath )}, 0 )]" );

		WriteDisplayAttributes( writer, methodDef.Display );

		if ( baseMethod is not null )
		{
			if ( baseMethod.IsPublic )
			{
				writer.Write( "    public ");
			}
			else if ( baseMethod.IsFamily )
			{
				writer.Write( "    protected " );
			}
			else
			{
				writer.Write( "    private " );
			}

			writer.Write( "override " );

			Assert.AreEqual( typeof(void), baseMethod.ReturnType );
		}
		else
		{
			if ( binding.Kind == NodeKind.Expression )
			{
				writer.WriteLine($"[{TypeRef<PureAttribute>()}]");
			}

			switch ( methodDef.Access )
			{
				case MethodAccess.Public:
					writer.Write( "    public " );
					break;

				case MethodAccess.Private:
					writer.Write( "    private " );
					break;
			}
		}

		writer.WriteLine( $"{TypeRef( typeof( void ) )} {methodDef.Name}( {string.Join( ", ", methodParameters )} )" );
		writer.WriteLine( "    {" );
		writer.Write( $"        {delegateFieldName} ??= " );
		writer.Write( $"{TypeRef( typeof( ActionGraphs.ActionGraphCache ) )}.{nameof( ActionGraphs.ActionGraphCache.GetOrAdd )}<{ClassName}, {delegateTypeName}>" );
		writer.WriteLine( $"( {StringLiteral( methodDef.SerializedBody, "        " )} );" );
		writer.WriteLine();
		writer.WriteLine( $"        {delegateFieldName}( {string.Join( ", ", arguments )} );" );
		writer.WriteLine( "    }" );

		writer.WriteLine();
		writer.WriteLine( $"    #endregion {methodDef.Display.Name}" );
	}

	private void WriteEvent( TextWriter writer, ComponentEventDefinition eventDef )
	{
		writer.WriteLine( $"    #region {eventDef.Display.Name}" );
		writer.WriteLine();

		var binding = NodeBinding.Create(
			new Facepunch.ActionGraphs.DisplayInfo( eventDef.Title ?? eventDef.Name, eventDef.Description,
				eventDef.Group, eventDef.Icon ),
			inputs: eventDef.Inputs );

		var parameters = new List<string>();
		var arguments = new List<string>();

		foreach ( var inputDef in binding.Inputs.Where( x => x is { IsSignal: false } ) )
		{
			var paramWriter = new StringWriter();

			WriteDisplayAttributes( paramWriter, inputDef.Display, "" );

			var parameter = $"{paramWriter.ToString().Replace( Environment.NewLine, "" )}{TypeRef( inputDef.Type )} {inputDef.Name}";

			parameters.Add( parameter );

			arguments.Add( inputDef.Name );
		}

		var delegateTypeName = $"{eventDef.Name}_Delegate";

		writer.WriteLine( $"    public delegate {TypeRef( typeof( void ) )} {delegateTypeName}( {string.Join( ", ", parameters )} );" );
		writer.WriteLine();

		WriteDisplayAttributes( writer, eventDef.Display, target: "field" );

		writer.WriteLine( $"    [field: {TypeRef<SourceLocationAttribute>()}( {StringLiteral( ResourcePath )}, 0 )]" );
		writer.WriteLine( $"    [field: {TypeRef<ActionGraphIgnoreAttribute>()}]" );
		writer.WriteLine( $"    [{TypeRef<PropertyAttribute>()}]" );

		writer.WriteLine($"    public event {delegateTypeName} {eventDef.Name};");
		writer.WriteLine();

		WriteDisplayAttributes( writer, eventDef.Display with { Group = "Events" } );

		writer.WriteLine( $"    public void {eventDef.Name}_Dispatch( {string.Join( ", ", parameters )} )");
		writer.WriteLine( "    {" );
		writer.WriteLine( $"        {eventDef.Name}?.Invoke( {string.Join( ", ", arguments )} );" );
		writer.WriteLine( "    }" );

		writer.WriteLine();
		writer.WriteLine( $"    #endregion {eventDef.Display.Name}" );
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
