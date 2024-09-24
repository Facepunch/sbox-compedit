using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;

namespace Sandbox;

#nullable enable

partial class ComponentPropertyDefinition
{
	internal ComponentPropertyDefinition( ComponentDefinition parent, ComponentResource.PropertyModel model )
	{
		ComponentDefinition = parent;

		Id = model.Id;
		Type = model.Type;

		DefaultValue = Json.FromNode( model.Default, Type );

		Access = model.Access;
		InitOnly = model.InitOnly;

		Title = model.Title;
		Description = model.Description;
		Group = model.Group;
		Icon = model.Icon;
		Hide = model.Hide;
	}

	public ComponentResource.PropertyModel Serialize()
	{
		var defaultValueNode = Type.IsInstanceOfType( DefaultValue )
			? Json.ToNode( DefaultValue, Type )
			: null;

		return new ComponentResource.PropertyModel(
			Id: Id,
			Type: Type,
			Default: defaultValueNode,
			Access: Access,
			InitOnly: InitOnly,
			Title: string.IsNullOrEmpty( Title ) ? null : Title,
			Description: string.IsNullOrEmpty( Description ) ? null : Description,
			Group: string.IsNullOrEmpty( Group ) ? null : Group,
			Icon: string.IsNullOrEmpty( Icon ) ? null : Icon,
			Hide: Hide );
	}
}

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

		JsonObject node;

		using ( EditorNodeLibrary.Push() )
		using ( ComponentDefinition.Resource.PushSerializationScopeInternal() )
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

		using ( EditorNodeLibrary.Push() )
		using ( ComponentDefinition.Resource.PushSerializationScopeInternal() )
		{
			if ( OverrideMethod is { } method )
			{
				var binding = NodeBinding.FromMethodBase( method, EditorNodeLibrary );

				// Make a copy, don't want to change original
				node = node.Deserialize<JsonNode>()!;

				node["Parameters"] = new JsonObject
				{
					{ "Inputs", Json.ToNode( binding.Inputs ) },
					{ "Outputs", Json.ToNode( binding.Outputs ) }
				};
			}

			return node.Deserialize<ActionGraph>( EditorJsonOptions )!;
		}
	}

	internal ComponentMethodDefinition( ComponentDefinition parent, ComponentResource.MethodModel model )
	{
		ComponentDefinition = parent;

		switch ( model )
		{
			case ComponentResource.NewMethodModel newMethodModel:
				Id = newMethodModel.Id;
				Access = newMethodModel.Access;
				break;

			case ComponentResource.OverrideMethodModel overrideModel:
				OverrideName = overrideModel.Name;
				break;
		}

		_serializedGraph = model.Body;
	}

	public ComponentResource.MethodModel Serialize()
	{
		return Override
			? new ComponentResource.OverrideMethodModel( OverrideName!, SerializedBody )
			: new ComponentResource.NewMethodModel( Id!.Value, Access, SerializedBody );
	}
}

partial class ComponentEventDefinition
{
	internal ComponentEventDefinition( ComponentDefinition parent, ComponentResource.EventModel model )
	{
		ComponentDefinition = parent;

		Id = model.Id;

		Title = model.Title;
		Description = model.Description;
		Group = model.Group;
		Icon = model.Icon;

		Inputs.AddRange( model.Inputs.Select( Json.FromNode<InputDefinition> ) );
	}

	public ComponentResource.EventModel Serialize()
	{
		return new ComponentResource.EventModel(
			Id: Id,
			Inputs: Inputs.Select( Json.ToNode ).ToArray(),
			Title: Title,
			Description: Description,
			Group: Group,
			Icon: Icon );
	}
}
