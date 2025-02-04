﻿using System;
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

	public string ClassName => $"Component_{Resource.ResourceId:x8}";

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

	private static string Constant( object? value, Type type )
	{
		if ( !type.IsInstanceOfType( value ) )
		{
			if ( !type.IsValueType || Nullable.GetUnderlyingType( type ) is not null )
			{
				value = null;
			}
			else
			{
				value = Activator.CreateInstance( type );
			}
		}

		return value switch
		{
			null => "null",
			string str => StringLiteral( str ),
			int i => i.ToString(),
			float f => f switch
			{
				float.NaN => $"{TypeRef<float>()}.{nameof(float.NaN)}",
				float.PositiveInfinity => $"{TypeRef<float>()}.{nameof(float.PositiveInfinity)}",
				float.NegativeInfinity => $"{TypeRef<float>()}.{nameof(float.NegativeInfinity)}",
				_ => $"{f:R}f",
			},
			double d => d switch
			{
				double.NaN => $"{TypeRef<double>()}.{nameof( double.NaN )}",
				double.PositiveInfinity => $"{TypeRef<double>()}.{nameof( double.PositiveInfinity )}",
				double.NegativeInfinity => $"{TypeRef<double>()}.{nameof( double.NegativeInfinity )}",
				_ => $"{d:R}d",
			},
			bool b => b ? "true" : "false",
			Resource res => $"{TypeRef( typeof(ResourceLibrary) )}.{nameof(ResourceLibrary.Get)}<{TypeRef( value.GetType() )}>( {StringLiteral( res.ResourcePath )} )",
			_ => $"{TypeRef( typeof(Json) )}.{nameof(Json.Deserialize)}<{TypeRef( value.GetType() )}>( {StringLiteral( Json.ToNode( value ) )} )"
		};
	}

	public void Build()
	{
		WriteToResource();

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

			writer.WriteLine( "#nullable disable" );
			writer.WriteLine();

			writer.WriteLine( $"namespace {ns};" );
			writer.WriteLine();

			WriteAttributes( writer );

			writer.WriteLine( $"public sealed class {ClassName} : {TypeRef<Component>()}" );
			writer.WriteLine( "{" );

			if ( !stubOnly )
			{
				writer.WriteLine( $"    private static {TypeRef<ComponentResource>()} _definition;" );
				writer.WriteLine( $"    internal static {TypeRef<ComponentResource>()} Definition => _definition" );
				writer.WriteLine( $"        ??= {TypeRef( typeof( ResourceLibrary ) )}.{nameof( ResourceLibrary.Get )}<{TypeRef<ComponentResource>()}>( {StringLiteral( ResourcePath )} )" );
				writer.WriteLine( $"        ?? throw new {TypeRef<Exception>()}( \"Component definition not found: \\\"{ResourcePath}\\\"\" );" );
				writer.WriteLine();

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

		WriteDisplayAttributes( writer, Resource.Display, "" );
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
			case ComponentResource.PropertyAccess.Public or ComponentResource.PropertyAccess.PublicGet:
				writer.Write( "    public " );
				break;
			case ComponentResource.PropertyAccess.Private:
				writer.Write( "    private " );
				break;
		}

		writer.Write( $"{TypeRef( propertyDef.Type )} {propertyDef.Name} {{ " );

		switch ( propertyDef.Access )
		{
			case ComponentResource.PropertyAccess.PublicGet:
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

		writer.WriteLine( $" = {Constant( propertyDef.DefaultValue, propertyDef.Type )};" );
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
			? NodeBinding.FromMethodBase( baseMethod, EditorNodeLibrary ).With( InputDefinition.Target( GeneratedType! ) )
			: methodDef.GetBinding();

		var delegateParameters = new List<string>();
		var methodParameters = new List<string>();
		var arguments = new List<string>();

		foreach ( var inputDef in binding.Inputs.Where( x => x is { IsSignal: false } ) )
		{
			var parameter = $"{TypeRef( inputDef.Type )} {inputDef.Name}";

			if ( inputDef.Display.Title is { } title )
			{
				parameter = $"[{TypeRef<TitleAttribute>()}( {StringLiteral( title )} )] {parameter}";
			}

			if ( inputDef.Display.Description is { } desc )
			{
				parameter = $"[{TypeRef<DescriptionAttribute>()}( {StringLiteral( desc )} )] {parameter}";
			}

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
				case ComponentResource.MethodAccess.Public:
					writer.Write( "    public " );
					break;

				case ComponentResource.MethodAccess.Private:
					writer.Write( "    private " );
					break;
			}
		}

		writer.WriteLine( $"{TypeRef( typeof( void ) )} {methodDef.Name}( {string.Join( ", ", methodParameters )} )" );
		writer.WriteLine( "    {" );
		writer.Write( $"        {delegateFieldName} ??= Definition.{nameof(ComponentResource.GetMethodBody)}<{delegateTypeName}>( " );

		if ( methodDef.Override )
		{
			writer.Write( StringLiteral( methodDef.OverrideName! ) );
		}
		else
		{
			writer.Write( methodDef.Id!.Value );
		}

		writer.WriteLine( " );" );
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
