using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using ActionGraphCache = Editor.ActionGraphCache;

namespace Sandbox;

#nullable enable

file abstract class ExtendedJsonConverter<T> : JsonConverter<T>
{
	protected static JsonSerializerOptions OptionsWithTypeConverter( JsonSerializerOptions options )
	{
		// TODO: Should we cache this?

		return new JsonSerializerOptions( options )
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
			Converters = { new TypeConverter( EditorNodeLibrary.TypeLoader ), new ObjectConverter() }
		};
	}
}

partial class ComponentDefinition
{
	private void UpdateMembers()
	{
		foreach ( var property in Properties )
		{
			_nextId = Math.Max( _nextId, property.Id + 1 );

			property.ComponentDefinition = this;
		}

		foreach ( var method in Methods )
		{
			if ( method.Id is { } id )
			{
				_nextId = Math.Max( _nextId, id + 1 );
			}

			method.ComponentDefinition = this;
		}

		foreach ( var evnt in Events )
		{
			_nextId = Math.Max( _nextId, evnt.Id + 1 );

			evnt.ComponentDefinition = this;
		}
	}
}

[JsonConverter( typeof(ComponentPropertyDefinitionConverter) )]
partial class ComponentPropertyDefinition
{

}

file class ComponentPropertyDefinitionConverter : ExtendedJsonConverter<ComponentPropertyDefinition>
{
	private record Model( int Id, Type Type, JsonNode? Default,
		PropertyAccess Access = PropertyAccess.Public, bool InitOnly = false,
		string? Title = null, string? Description = null, string? Group = null, string? Icon = null, bool Hide = false );


	public override ComponentPropertyDefinition Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		options = OptionsWithTypeConverter( options );

		var model = JsonSerializer.Deserialize<Model>( ref reader, options )!;

		return new ComponentPropertyDefinition
		{
			Id = model.Id,
			Type = model.Type,
			DefaultValue = model.Default?.Deserialize( model.Type, options ),
			Access = model.Access,
			InitOnly = model.InitOnly,
			Title = model.Title,
			Description = model.Description,
			Group = model.Group,
			Icon = model.Icon,
			Hide = model.Hide
		};
	}

	public override void Write( Utf8JsonWriter writer, ComponentPropertyDefinition value, JsonSerializerOptions options )
	{
		options = OptionsWithTypeConverter( options );

		var defaultValueNode = value.Type.IsInstanceOfType( value.DefaultValue )
			? JsonSerializer.SerializeToNode( value.DefaultValue, value.Type, options )
			: null;

		var model = new Model(
			Id: value.Id,
			Type: value.Type,
			Default: defaultValueNode,
			Access: value.Access,
			InitOnly: value.InitOnly,
			Title: string.IsNullOrEmpty( value.Title ) ? null : value.Title,
			Description: string.IsNullOrEmpty( value.Description ) ? null : value.Description,
			Group: string.IsNullOrEmpty( value.Group ) ? null : value.Group,
			Icon: string.IsNullOrEmpty( value.Icon ) ? null : value.Icon,
			Hide: value.Hide );

		JsonSerializer.Serialize( writer, model, options );
	}
}

[JsonConverter( typeof( ComponentMethodDefinitionConverter ) )]
partial class ComponentMethodDefinition
{
	// Defer actually deserializing the graph until needed, in case types aren't loaded yet

	private ActionGraph? _graph;
	private JsonNode? _serializedGraph;

	[Hide]
	public JsonNode? SerializedBody
	{
		get => _serializedGraph ??= SerializeBody( _graph );
		set
		{
			_serializedGraph = value;
			_graph = null;
		}
	}

	private JsonNode? SerializeBody( ActionGraph? body )
	{
		if ( body is null )
		{
			return null;
		}

		using var libraryScope = EditorNodeLibrary.Push();

		JsonObject node;

		using ( ActionGraph.PushTarget( InputDefinition.Target( ComponentDefinition.GeneratedType! ) ) )
		{
			node = JsonSerializer.SerializeToNode( body, EditorJsonOptions )!.AsObject();
		}

		if ( Override )
		{
			node.Remove( "Parameters" );
		}

		return node;
	}

	private ActionGraph? DeserializeBody( JsonNode? node )
	{
		if ( node is null )
		{
			return null;
		}

		return ActionGraphCache.GetOrAdd( ComponentDefinition.GeneratedType!, node, OverrideMethod is { } method
			? NodeBinding.FromMethodBase( method, EditorNodeLibrary )
			: null );
	}
}

file class ComponentMethodDefinitionConverter : JsonConverter<ComponentMethodDefinition>
{
	[JsonPolymorphic]
	[JsonDerivedType( typeof(OverrideModel), "Override" )]
	[JsonDerivedType( typeof(NewModel), "New" )]
	private record Model( JsonNode? Body );

	private record OverrideModel( string Name, JsonNode? Body ) : Model( Body );
	private record NewModel( int Id, MethodAccess Access, JsonNode? Body ) : Model( Body );

	public override ComponentMethodDefinition? Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		var model = JsonSerializer.Deserialize<Model>( ref reader, options );

		return model switch
		{
			null => null,
			OverrideModel overrideModel => new ComponentMethodDefinition
			{
				OverrideName = overrideModel.Name, SerializedBody = overrideModel.Body
			},
			NewModel newModel => new ComponentMethodDefinition
			{
				Id = newModel.Id,
				Access = newModel.Access,
				SerializedBody = newModel.Body
			},
			_ => throw new NotImplementedException()
		};
	}

	public override void Write( Utf8JsonWriter writer, ComponentMethodDefinition value, JsonSerializerOptions options )
	{
		JsonSerializer.Serialize<Model>( writer, value.Override
			? new OverrideModel( Name: value.OverrideName!, Body: value.SerializedBody )
			: new NewModel( Id: value.Id!.Value, value.Access, value.SerializedBody ),
			options );
	}
}

[JsonConverter( typeof(ComponentEventDefinitionConverter) )]
partial class ComponentEventDefinition
{

}

file class ComponentEventDefinitionConverter : ExtendedJsonConverter<ComponentEventDefinition>
{
	private record Model( int Id, IReadOnlyList<JsonNode> Inputs,
		string? Title = null, string? Description = null, string? Group = null, string? Icon = null );

	public override ComponentEventDefinition? Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		options = OptionsWithTypeConverter( options );

		var model = JsonSerializer.Deserialize<Model>( ref reader, options );

		if ( model is null )
		{
			return null;
		}

		return new ComponentEventDefinition
		{
			Id = model.Id,
			Title = model.Title,
			Description = model.Description,
			Group = model.Group,
			Icon = model.Icon
		};
	}

	public override void Write( Utf8JsonWriter writer, ComponentEventDefinition value, JsonSerializerOptions options )
	{
		options = OptionsWithTypeConverter( options );

		JsonSerializer.Serialize( writer,
			new Model(
				Id: value.Id,
				Inputs: value.Inputs.Select( x => x.SerializeToNode( options ) ).ToArray(),
				Title: value.Title,
				Description: value.Description,
				Group: value.Group,
				Icon: value.Icon ),
			options );
	}
}
