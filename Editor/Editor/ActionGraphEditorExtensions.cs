using System.Linq;
using System.Threading.Tasks;
using Editor.NodeEditor;
using Facepunch.ActionGraphs;
using Sandbox;
using Sandbox.ActionGraphs;

namespace Editor.ActionGraphs;

public static class ActionGraphEditorExtensions
{
	public static ComponentDefinition GetComponentResource( this EditorActionGraph graph )
	{
		return graph.Graph.GetComponentDefinition();
	}

	public static ComponentDefinition GetComponentDefinition( this ActionGraph graph )
	{
		return graph.SourceLocation is not GameResourceSourceLocation { Resource: ComponentResource resource } ? null : ComponentDefinition.Get( resource );
	}

	public static ComponentPropertyDefinition GetInputPropertyDefinition( this ActionInputPlug plug )
	{
		var node = plug.InputNestedNode;

		if ( node?.Definition != EditorNodeLibrary.Property )
		{
			return null;
		}

		var name = node.Properties.Name.Value as string;
		var componentDef = plug.Parameter.Node.ActionGraph.GetComponentDefinition();

		return componentDef?.Properties.FirstOrDefault( x => x.Name == name );
	}

	[Event( GetEditorPropertiesEvent.EventName )]
	public static void OnGetEditorProperties( GetEditorPropertiesEvent eventArgs )
	{
		if ( eventArgs.EditorGraph.GetComponentResource() is not { } compDef )
		{
			return;
		}

		if ( compDef.Methods.Any( x => !x.Override && x.BodyGuid == eventArgs.ActionGraph.Guid ) )
		{
			eventArgs.CanModifyParameters = true;
		}
	}

	[Event( PopulateNodeMenuEvent.EventName )]
	public static void OnPopulateNodeMenu( PopulateNodeMenuEvent eventArgs )
	{
		if ( eventArgs.View.Graph.GetComponentResource() is not { } compDef ) return;

		var menu = eventArgs.Menu;
		var addPropertyMenu = menu.AddMenu( "Add Property", "add_box" );

		addPropertyMenu.AboutToShow += () =>
		{
			addPropertyMenu.Clear();
			addPropertyMenu.AddLineEdit( "Name", onSubmit: name =>
			{
				if ( string.IsNullOrEmpty( name ) )
				{
					return;
				}

				var needsRebuild = false;

				if ( compDef.Properties.FirstOrDefault( x => x.Name == name ) is not { } property )
				{
					property = compDef.AddProperty( eventArgs.TargetPlug.PropertyType );
					property.Title = name;

					needsRebuild = true;
				}

				var nodeType = new LocalTargetNodeType( CreatePropertyNodeType( property ), null! );

				eventArgs.View.CreateNewNode( nodeType, eventArgs.ClickPos, eventArgs.TargetPlug );

				if ( needsRebuild )
				{
					compDef.Build();
				}
			}, autoFocus: true );
		};
	}

	[Event( PopulateInputPlugMenuEvent.EventName )]
	public static void OnPopulateInputPlugMenu( PopulateInputPlugMenuEvent eventArgs )
	{
		if ( eventArgs.Plug.Type == typeof(Task) 
			|| eventArgs.EditorGraph.GetComponentResource() is not { } componentDefinition
			|| eventArgs.ActionGraph.TargetOutput is not { } targetSource )
		{
			return;
		}

		var editorNode = eventArgs.Plug.Node;
		var node = editorNode.Node;

		void CreatePropertyLink( ComponentPropertyDefinition property )
		{
			eventArgs.View?.PushUndo( "Create Property Link" );

			var sourceNode = eventArgs.ActionGraph.AddNode( EditorNodeLibrary.Property, editorNode.Node );

			sourceNode.Inputs.Target.SetLink( targetSource );
			sourceNode.Properties.Name.Value = property.Name;

			var resultOutput = sourceNode.Outputs.Result;

			if ( eventArgs.Plug.IsArrayElement )
			{
				if ( eventArgs.Plug.Index < eventArgs.Plug.Parameter.LinkArray!.Count )
				{
					eventArgs.Plug.Parameter.SetLink( resultOutput, eventArgs.Plug.Index );
				}
				else
				{
					eventArgs.Plug.Parameter.InsertLink( resultOutput, eventArgs.Plug.Index );
				}
			}
			else
			{
				eventArgs.Plug.Parameter.SetLink( resultOutput );
			}

			editorNode.MarkDirty();

			eventArgs.View?.PushRedo();
		}

		var createPropertyMenu = eventArgs.Menu.AddMenu( "Create Property", "add_box" );

		createPropertyMenu.AboutToShow += () =>
		{
			createPropertyMenu.Clear();

			createPropertyMenu.AddLineEdit( "Name", autoFocus: true, onSubmit: value =>
			{
				if ( string.IsNullOrEmpty( value ) ) return;

				var property = componentDefinition.AddProperty( eventArgs.Plug.Type );

				property.Title = value;

				if ( eventArgs.Plug.Parameter.Link?.TryGetConstant( out var constValue ) is true &&
					constValue?.GetType().IsAssignableTo( eventArgs.Plug.Type ) is true )
				{
					property.DefaultValue = constValue;
				}

				CreatePropertyLink( property );

				componentDefinition.Build();
			} );
		};

		var matchingProperties = componentDefinition.Properties
			.Where( x => x.Type.IsAssignableTo( eventArgs.Plug.Type ) )
			.ToArray();

		if ( matchingProperties.Any() )
		{
			var propMenu = eventArgs.Menu.AddMenu( "Use Property", "inbox" );

			foreach ( var property in matchingProperties )
			{
				propMenu.AddOption( property.Title, "inbox", () =>
				{
					CreatePropertyLink( property );
				} );
			}
		}
	}

	[Event( PopulateCreateSubGraphMenuEvent.EventName )]
	public static void OnPopulateCreateSubGraphMenu( PopulateCreateSubGraphMenuEvent eventArgs )
	{
		if ( eventArgs.EditorGraph.GetComponentResource() is not { } compDef 
			|| eventArgs.ActionGraph.TargetOutput is not { } targetSource )
		{
			return;
		}

		var createMethodMenu = eventArgs.Menu.AddMenu( "Create Method", "add_box" );

		createMethodMenu.AboutToShow += () =>
		{
			createMethodMenu.Clear();

			createMethodMenu.AddLineEdit( "Name",
				autoFocus: true,
				onSubmit: value =>
				{
					if ( string.IsNullOrEmpty( value ) ) return;

					_ = eventArgs.View.CreateSubGraph( eventArgs.Nodes, async subGraph =>
					{
						subGraph.Title = value;

						var method = compDef.AddMethod( subGraph );

						compDef.Build();

						await Task.Delay( 500 );
						await EditorUtility.Projects.WaitForCompiles();

						var node = eventArgs.ActionGraph.AddNode( EditorNodeLibrary.CallMethod );

						node.Properties.Name.Value = method.Name;
						node.Properties.IsStatic.Value = false;

						node.Inputs.Target.SetLink( targetSource );

						return node;
					} );
				} );
		};
	}

	[Event( GoToPlugSourceEvent.EventName )]
	public static void OnGoToPlugSource( GoToPlugSourceEvent eventArgs )
	{
		if ( eventArgs.Handled ) return;

		if ( eventArgs.Plug.GetInputPropertyDefinition() is { } propertyDef )
		{
			CodeEditor.OpenFile( propertyDef );
			eventArgs.Handled = true;
		}
	}

	[Event( FindGraphTargetEvent.EventName )]
	private static void OnFindGraphTarget( FindGraphTargetEvent eventArgs )
	{
		if ( eventArgs.TargetValue is ComponentResource compDef )
		{
			eventArgs.TargetValue = null;
			eventArgs.TargetType = compDef.GeneratedType;
		}
	}

	[Event( BuildInputLabelEvent.EventName )]
	public static void BuildPropertyInputLabel( BuildInputLabelEvent eventArgs )
	{
		if ( eventArgs.Handled ) return;
		if ( eventArgs.Plug.GetInputPropertyDefinition() is not {} propertyDef ) return;

		eventArgs.Text = propertyDef.Title ?? propertyDef.Name;
		eventArgs.Icon = "logout";
		eventArgs.Handled = true;
	}

	public static PropertyNodeType CreatePropertyNodeType( this ComponentPropertyDefinition property )
	{
		return new PropertyNodeType( property.ComponentDefinition.GeneratedType!,
			property.Name, property.Type,
			property.Display, true, property.InitOnly );
	}
}
