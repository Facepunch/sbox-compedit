using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Editor;
using Facepunch.ActionGraphs;
using Sandbox.Internal;

namespace Sandbox;

#nullable enable

public partial class ComponentDefinition : ISourcePathProvider
{
	private static Dictionary<ComponentResource, ComponentDefinition> Definitions { get; } = new();

	public static ComponentDefinition Get( ComponentResource resource )
	{
		return Definitions.TryGetValue( resource, out var def )
			? def
			: Definitions[resource] = new ComponentDefinition( resource );
	}

	private int _nextId = 1;

	public ComponentResource Resource { get; }

	public List<ComponentPropertyDefinition> Properties { get; } = new ();
	
	public List<ComponentMethodDefinition> Methods { get; } = new();

	public List<ComponentEventDefinition> Events { get; } = new();

	public Type? GeneratedType => Resource.GeneratedType;

	public string? Title
	{
		get => Resource.Title;
		set => Resource.Title = value;
	}

	public string? Description
	{
		get => Resource.Description;
		set => Resource.Description = value;
	}

	public string? Group
	{
		get => Resource.Group;
		set => Resource.Group = value;
	}

	public string? Icon
	{
		get => Resource.Icon;
		set => Resource.Icon = value;
	}

	private ComponentDefinition( ComponentResource resource )
	{
		Resource = resource;

		UpdateFromResource();
	}

	public void UpdateFromResource()
	{
		Properties.Clear();
		Methods.Clear();
		Events.Clear();

		Properties.AddRange( Resource.Properties.Select( x => new ComponentPropertyDefinition( this, x ) ) );
		Methods.AddRange( Resource.Methods.Select( x => new ComponentMethodDefinition( this, x ) ) );
		Events.AddRange( Resource.Events.Select( x => new ComponentEventDefinition( this, x ) ) );
	}

	public void WriteToResource()
	{
		Resource.Properties.Clear();
		Resource.Methods.Clear();
		Resource.Events.Clear();

		Resource.Properties.AddRange( Properties.Select( x => x.Serialize() ) );
		Resource.Methods.AddRange( Methods.Select( x => x.Serialize() ) );
		Resource.Events.AddRange( Events.Select( x => x.Serialize() ) );

		AssetSystem.FindByPath( ResourcePath )?.SaveToDisk( Resource );
	}

	public ComponentPropertyDefinition AddProperty( Type type )
	{
		var property = new ComponentPropertyDefinition( this, new ComponentResource.PropertyModel( _nextId++, type ) );

		Properties.Add( property );

		return property;
	}

	public void AddDefaultParameters( ActionGraph body )
	{
		var inputSignal = InputDefinition.Signal();
		var outputSignal = OutputDefinition.Signal();
		var targetInput = InputDefinition.Target( GeneratedType! );

		body.SetParameters( new[] { inputSignal, targetInput }, new [] { outputSignal } );
	}

	public ComponentMethodDefinition AddMethod( NodeLibrary nodeLibrary )
	{
		var body = ActionGraph.CreateEmpty( nodeLibrary );

		AddDefaultParameters( body );

		body.AddRequiredNodes();

		return AddMethod( body );
	}

	public ComponentMethodDefinition AddMethod( ActionGraph body )
	{
		var method = new ComponentMethodDefinition( this, new ComponentResource.NewMethodModel( _nextId++ ) )
		{
			Body = body
		};

		Methods.Add( method );

		return method;
	}

	public ComponentMethodDefinition AddOverride( string name, NodeLibrary nodeLibrary )
	{
		if ( Methods.FirstOrDefault( x => x.Override && x.Name == name ) is { } existing )
		{
			return existing;
		}

		var body = ActionGraph.CreateEmpty( nodeLibrary );
		var method = new ComponentMethodDefinition( this, new ComponentResource.OverrideMethodModel( name ) )
		{
			Body = body
		};

		method.UpdateParameters( body );

		body.AddRequiredNodes();

		Methods.Add( method );

		return method;
	}

	public ComponentEventDefinition AddEvent( IEnumerable<InputDefinition> inputs )
	{
		var evnt = new ComponentEventDefinition( this, new ComponentResource.EventModel( _nextId++, Array.Empty<JsonNode>() ) );

		evnt.Inputs.AddRange( inputs );

		Events.Add( evnt );

		return evnt;
	}

	public string ResourcePath => Resource.ResourcePath;

	string ISourcePathProvider.Path => ResourcePath;
}


public partial class ComponentPropertyDefinition : IMemberNameProvider
{
	internal ComponentDefinition ComponentDefinition { get; }

	public int Id { get; }

	public string Name => $"Property{Id}";

	public Type Type { get; set; }

	public object? DefaultValue { get; set; }

	public ComponentResource.PropertyAccess Access { get; set; } = Sandbox.ComponentResource.PropertyAccess.Public;

	public bool InitOnly { get; set; }

	public string? Title { get; set; }

	public string? Description { get; set; }

	public string? Group { get; set; }

	public string? Icon { get; set; }

	public bool Hide { get; set; }

	public DisplayInfo Display => new ()
	{
		Name = Title ?? Name.ToTitleCase(),
		Description = Description,
		Group = Group,
		Icon = Icon,
		Browsable = !Hide
	};

	string ISourcePathProvider.Path => ((ISourcePathProvider)ComponentDefinition).Path;

	string IMemberNameProvider.MemberName => Name;
}

public partial class ComponentMethodDefinition : IMemberNameProvider
{
	internal ComponentDefinition ComponentDefinition { get; }
	public int? Id { get; }

	public string? OverrideName { get; }

	public string Name => OverrideName ?? $"Method{Id}";

	public bool Override => OverrideName != null;

	public ActionGraph? Body
	{
		get => _graph ??= DeserializeBody( _serializedGraph );
		set
		{
			_graph = value;
			_serializedGraph = null;
		}
	}

	public NodeBinding GetBinding( bool forHash = false )
	{
		if ( _graph is not null )
		{
			return NodeBinding.Create( _graph.DisplayInfo,
				inputs: _graph.Inputs.Values.ToArray(),
				outputs: _graph.Outputs.Values.ToArray() );
		}

		var baseBinding = Override
			? NodeBinding.FromMethodBase( OverrideMethod!, EditorNodeLibrary )
			: NodeBinding.FromSerializedActionGraph( _serializedGraph, EditorNodeLibrary, EditorJsonOptions )!;

		return forHash
			? baseBinding
			: baseBinding.With( InputDefinition.Target( ComponentDefinition.GeneratedType! ) );
	}

	public MethodInfo? OverrideMethod => Override
		? typeof( Component ).GetMethod( Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance )
		: null;

	public ComponentResource.MethodAccess Access { get; set; } = Sandbox.ComponentResource.MethodAccess.Public;

	public T? GetUserData<T>( string name )
		where T : class
	{
		return (_graph?.UserData ?? _serializedGraph?["UserData"])?[name]?.GetValue<T>();
	}

	public DisplayInfo Display => new()
	{
		Name = GetUserData<string>( "Title" ) ?? Name.ToTitleCase(),
		Description = GetUserData<string>( "Description" ),
		Group = GetUserData<string>( "Category" ),
		Icon = GetUserData<string>( "Icon" )
	};

	internal void UpdateParameters( ActionGraph body )
	{
		if ( Override )
		{
			body.SetParameters( NodeBinding
				.FromMethodBase( OverrideMethod!, EditorNodeLibrary )
				.With( InputDefinition.Target( ComponentDefinition.GeneratedType! ) ) );
		}
	}

	string ISourcePathProvider.Path => ((ISourcePathProvider)ComponentDefinition).Path;

	string IMemberNameProvider.MemberName => Name;
}

public partial class ComponentEventDefinition : IMemberNameProvider
{
	internal ComponentDefinition ComponentDefinition { get; }
	public int Id { get; }

	public string Name => $"Event{Id}";

	public string? Title { get; set; }

	public string? Description { get; set; }

	public string? Group { get; set; }

	public string? Icon { get; set; }

	public List<InputDefinition> Inputs { get; } = new();

	public DisplayInfo Display => new()
	{
		Name = Title ?? Name.ToTitleCase(),
		Description = Description,
		Group = Group,
		Icon = Icon
	};

	string ISourcePathProvider.Path => ((ISourcePathProvider)ComponentDefinition).Path;

	string IMemberNameProvider.MemberName => Name;
}
