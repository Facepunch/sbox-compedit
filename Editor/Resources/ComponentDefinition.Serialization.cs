using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;

namespace Sandbox;

#nullable enable

partial class ComponentDefinition
{
	[Event( "actiongraph.saving" )]
	public static void OnActionGraphSaving( ActionGraph graph, GameResource resource )
	{
		if ( resource is not ComponentResource componentResource )
		{
			return;
		}

		var definition = Get( componentResource );
		var matchingMethod = definition.Methods
			.FirstOrDefault( x => graph.Guid.Equals( x.BodyGuid ) );

		if ( matchingMethod is null )
		{
			Log.Warning( $"Can't find matching method for graph {graph.Title} in resource {resource.ResourcePath}!" );
			return;
		}

		matchingMethod.Body = graph;

		definition.Build();
	}
}

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

	private JsonNode? SerializeBody()
	{
		if ( _graph is null )
		{
			return null;
		}

		JsonObject node;

		using ( ComponentDefinition.Resource.PushSerializationScopeInternal() )
		{
			node = JsonSerializer.SerializeToNode( _graph, EditorJsonOptions )!.AsObject();
		}

		if ( Override )
		{
			node.Remove( "Parameters" );
		}

		return node;
	}

	private ActionGraph? DeserializeBody()
	{
		if ( _serializedGraph is null )
		{
			return null;
		}

		var node = _serializedGraph;

		using ( ComponentDefinition.Resource.PushSerializationScopeInternal() )
		{
			if ( OverrideMethod is { } method )
			{
				var binding = NodeBinding.FromMethodBase( method, EditorNodeLibrary );

				// Make a copy, don't want to change original
				node = node.Deserialize<JsonNode>()!;

				node["Parameters"] = new JsonObject
				{
					{ "Inputs", Json.ToNode( binding.Inputs.With( InputDefinition.Target( ComponentDefinition.GeneratedType! ) ) ) },
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
