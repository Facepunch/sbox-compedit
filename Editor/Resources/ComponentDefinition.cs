using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Facepunch.ActionGraphs;
using Sandbox.Internal;

namespace Sandbox;

#nullable enable

[GameResource( "Component Definition", "comp", "Describes the properties, methods and events of a component type.", Icon = "article" )]
public partial class ComponentDefinition : GameResource
{
	private int _nextId = 1;

	/// <summary>
	/// A nicely formatted human-readable name for this component.
	/// </summary>
	[Property, Group( "Display" )]
	public string? Title { get; set; }

	/// <summary>
	/// What is this component for?
	/// </summary>
	[Property, Group( "Display" )]
	public string? Description { get; set; }

	/// <summary>
	/// Group name to help categorize this component.
	/// </summary>
	[Property, Group( "Display" )]
	public string? Group { get; set; }

	/// <summary>
	/// Material icon for this component.
	/// </summary>
	[Property, Group( "Display" )]
	public string? Icon { get; set; }

	[Hide, JsonIgnore]
	public DisplayInfo Display => new()
	{
		Name = Title ?? ResourceName.ToTitleCase(),
		Description = Description,
		Group = Group,
		Icon = Icon
	};
	
	public List<ComponentPropertyDefinition> Properties { get; set; } = new ();
	
	public List<ComponentMethodDefinition> Methods { get; set; } = new();

	public List<ComponentEventDefinition> Events { get; set; } = new();

	public T? GetDefaultValue<T>( string property )
	{
		return Properties.FirstOrDefault( x => x.Name == property )?.DefaultValue is T defaultValue
			? defaultValue
			: default;
	}

	public ActionGraph? GetMethodBody( string name )
	{
		return Methods.FirstOrDefault( x => x.Name == name )?.Body;
	}

	public ComponentPropertyDefinition AddProperty( Type type )
	{
		var property = new ComponentPropertyDefinition( _nextId++, type, this );

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
		var method = new ComponentMethodDefinition( _nextId++, this, body );

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
		var method = new ComponentMethodDefinition( name, this, body );

		method.UpdateParameters( body );

		body.AddRequiredNodes();

		Methods.Add( method );

		return method;
	}

	public ComponentEventDefinition AddEvent( IEnumerable<InputDefinition> inputs )
	{
		var evnt = new ComponentEventDefinition( _nextId++, this );

		evnt.Inputs.AddRange( inputs );

		Events.Add( evnt );

		return evnt;
	}
}

public enum PropertyAccess
{
	/// <summary>
	/// This property is only accessible and modifiable from inside this component.
	/// </summary>
	[Icon( "shield" )]
	Private,

	/// <summary>
	/// This property can be accessed publicly, but can only be set from inside this component.
	/// </summary>
	[Icon( "policy" )]
	PublicGet,

	/// <summary>
	/// This property is publicly accessible and modifiable from anywhere.
	/// </summary>
	[Icon( "public" )]
	Public
}

public enum MethodAccess
{
	/// <summary>
	/// This method can only be called from inside this component.
	/// </summary>
	[Icon( "shield" )]
	Private,

	/// <summary>
	/// This method is publicly callable from anywhere.
	/// </summary>
	[Icon( "public" )]
	Public
}

public partial class ComponentPropertyDefinition : IMemberNameProvider
{
	[Property]
	public int Id { get; set; }

	[Hide, JsonIgnore]
	internal ComponentDefinition ComponentDefinition { get; set; } = null!;

	[Property]
	public string Name => $"Property{Id}";

	[Property]
	public Type Type { get; set; } = typeof(object);

	[Property]
	public object? DefaultValue { get; set; }

	[Property]
	public PropertyAccess Access { get; set; } = PropertyAccess.Public;

	[Property]
	public bool InitOnly { get; set; }

	[Property, Group( "Display" )]
	public string? Title { get; set; }

	[Property, Group( "Display" )]
	public string? Description { get; set; }

	[Property, Group( "Display" )]
	public string? Group { get; set; }

	[Property, Group( "Display" )]
	public string? Icon { get; set; }

	[Property, Group( "Display" )]
	public bool Hide { get; set; }

	[Hide]
	public DisplayInfo Display => new ()
	{
		Name = Title ?? Name.ToTitleCase(),
		Description = Description,
		Group = Group,
		Icon = Icon,
		Browsable = !Hide
	};

	public ComponentPropertyDefinition()
	{

	}

	internal ComponentPropertyDefinition( int id, Type type, ComponentDefinition parent )
	{
		Id = id;
		Type = type;
	}

	string ISourcePathProvider.Path => ((ISourcePathProvider)ComponentDefinition).Path;

	string IMemberNameProvider.MemberName => Name;
}

public partial class ComponentMethodDefinition : IMemberNameProvider
{
	[Hide]
	public int? Id { get; set; }

	[Hide, JsonIgnore]
	internal ComponentDefinition ComponentDefinition { get; set; } = null!;

	[Hide]
	public string? OverrideName { get; set; }

	[Property]
	public string Name => OverrideName ?? $"Method{Id}";

	[Hide] public bool Override => OverrideName != null;

	[Property]
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

	[Hide]
	public MethodInfo? OverrideMethod => Override
		? typeof( Component ).GetMethod( Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance )
		: null;

	[Property]
	public MethodAccess Access { get; set; } = MethodAccess.Public;

	public T? GetUserData<T>( string name )
		where T : class
	{
		return (_graph?.UserData ?? _serializedGraph?["UserData"])?[name]?.GetValue<T>();
	}

	[Hide]
	public DisplayInfo Display => new()
	{
		Name = GetUserData<string>( "Title" ) ?? Name.ToTitleCase(),
		Description = GetUserData<string>( "Description" ),
		Group = GetUserData<string>( "Category" ),
		Icon = GetUserData<string>( "Icon" )
	};

	public ComponentMethodDefinition()
	{

	}

	internal ComponentMethodDefinition( int id, ComponentDefinition parent, ActionGraph body )
	{
		Id = id;
		OverrideName = null;
		ComponentDefinition = parent;

		_graph = body;
	}

	internal ComponentMethodDefinition( string overrideName, ComponentDefinition parent, ActionGraph body )
	{
		Id = null;
		OverrideName = overrideName;
		ComponentDefinition = parent;

		_graph = body;
	}

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
	[Hide]
	public int Id { get; set; }

	[Hide, JsonIgnore]
	internal ComponentDefinition ComponentDefinition { get; set; } = null!;

	[Property]
	public string Name => $"Event{Id}";

	[Property, Group( "Display" )]
	public string? Title { get; set; }

	[Property, Group( "Display" )]
	public string? Description { get; set; }

	[Property, Group( "Display" )]
	public string? Group { get; set; }

	[Property, Group( "Display" )]
	public string? Icon { get; set; }

	[Property]
	public List<InputDefinition> Inputs { get; } = new List<InputDefinition>();

	[Hide]
	public DisplayInfo Display => new()
	{
		Name = Title ?? Name.ToTitleCase(),
		Description = Description,
		Group = Group,
		Icon = Icon
	};

	public ComponentEventDefinition()
	{

	}

	internal ComponentEventDefinition( int id, ComponentDefinition parent )
	{
		Id = id;
		ComponentDefinition = parent;
	}

	string ISourcePathProvider.Path => ((ISourcePathProvider)ComponentDefinition).Path;

	string IMemberNameProvider.MemberName => Name;
}
