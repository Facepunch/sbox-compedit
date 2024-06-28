using System.Linq;
using Facepunch.ActionGraphs;
using Sandbox;

namespace Editor.ActionGraphs;

public class ComponentDefinitionEditor : BaseResourceEditor<ComponentDefinition>
{
	public SerializedObject Serialized { get; private set; }

	private ExpandGroup _propertiesExpandGroup;
	private ExpandGroup _methodsExpandGroup;
	private ExpandGroup _eventsExpandGroup;

	private readonly ControlSheet _displaySheet;
	private readonly ComponentPropertyList _propertyList;
	private readonly ComponentMethodList _methodList;
	private readonly ComponentEventList _eventList;

	private int _lastBuildNumber;

	public ComponentDefinitionEditor()
	{
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;

		var (displayGroup, _) = AddGroup( "Display", "description" );
		var (propertiesGroup, propertiesExpandGroup) = AddGroup( "Properties", "format_list_bulleted" );
		var (methodsGroup, methodsExpandGroup) = AddGroup( "Methods", "directions_run" );
		var (eventsGroup, eventsExpandGroup ) = AddGroup( "Events", "bolt" );

		_propertiesExpandGroup = propertiesExpandGroup;
		_methodsExpandGroup = methodsExpandGroup;
		_eventsExpandGroup = eventsExpandGroup;

		displayGroup.Layout = _displaySheet = new ControlSheet();
		
		_displaySheet.SetMinimumColumnWidth( 0, 60 );
		_displaySheet.SetMinimumColumnWidth( 1, 120 );

		{
			propertiesGroup.Layout = Layout.Column();
			propertiesGroup.Layout.Add( _propertyList = new ComponentPropertyList( this ) );

			// Add property button
			var row = propertiesGroup.Layout.AddRow();
			row.AddStretchCell();
			row.Margin = 16;
			var button = row.Add( new Button.Primary( "Add Property", "add" ) );
			button.MinimumWidth = 320;
			button.Clicked = () => AddPropertyDialog( button );
			row.AddStretchCell();

			propertiesGroup.Layout.AddStretchCell( 1 );
		}

		{
			methodsGroup.Layout = Layout.Column();
			methodsGroup.Layout.Add( _methodList = new ComponentMethodList( this ) );

			// Add method button
			var row = methodsGroup.Layout.AddRow();
			row.AddStretchCell();
			row.Margin = 16;
			row.Spacing = 20;
			var addButton = row.Add( new Button.Primary( "Add Method", "add" ) );
			addButton.MinimumWidth = 150;
			addButton.Clicked = () => AddMethodDialog( addButton );

			var overrideButton = row.Add( new Button.Primary( "Override Method", "extension" ) );
			overrideButton.MinimumWidth = 150;
			overrideButton.Clicked = () => OverrideMethodDialog( overrideButton );
			row.AddStretchCell();

			methodsGroup.Layout.AddStretchCell( 1 );
		}

		{
			eventsGroup.Layout = Layout.Column();
			eventsGroup.Layout.Add( _eventList = new ComponentEventList( this ) );

			// Add property button
			var row = eventsGroup.Layout.AddRow();
			row.AddStretchCell();
			row.Margin = 16;
			var button = row.Add( new Button.Primary( "Add Event", "add" ) );
			button.MinimumWidth = 320;
			button.Clicked = () => AddEventDialog( button );
			row.AddStretchCell();

			eventsGroup.Layout.AddStretchCell( 1 );
		}
	}

	private void AddPropertyDialog( Button source )
	{
		var type = Resource.Properties.LastOrDefault()?.Type ?? typeof( float );
		var property = Resource.AddProperty( type );

		property.Title = property.Name.ToTitleCase();

		Resource.Build();

		_propertyList.Initialize( Serialized.GetProperty( nameof( ComponentDefinition.Properties ) ) );
	}

	private MethodDescription[] GetOverridable()
	{
		return EditorTypeLibrary.GetType<Component>()
			.Methods
			.Where( x => x.IsVirtual && (x.IsFamily || x.IsPublic) )
			.Where( x => Resource.Methods.All( y => y.Name != x.Name ) )
			.Where( x => x.ReturnType == typeof(void) ) // TODO: support returning methods
			.ToArray();
	}

	private void AddMethodDialog( Button source )
	{
		var method = Resource.AddMethod( EditorNodeLibrary );

		method.Body!.Title = method.Name.ToTitleCase();

		Resource.Build();
	}

	private void AddEventDialog( Button source )
	{
		var evnt = Resource.AddEvent( Enumerable.Empty<InputDefinition>() );

		evnt.Title = evnt.Name.ToTitleCase();

		Resource.Build();
	}

	private void OverrideMethodDialog( Button source )
	{
		var menu = new Menu( source );

		foreach ( var methodDescription in GetOverridable() )
		{
			menu.AddOption( methodDescription.Title, methodDescription.Icon ?? "bolt", () =>
			{
				var name = methodDescription.Name;
				var method = Resource.AddOverride( name, EditorNodeLibrary );

				method.Body!.Title = name.ToTitleCase();

				Resource.Build();
			} );
		}

		menu.OpenAtCursor( true );
	}

	protected override void Initialize( Asset asset, ComponentDefinition resource )
	{
		_lastBuildNumber = Resource.BuildNumber;

		Serialized = EditorTypeLibrary.GetSerializedObject( resource );

		_displaySheet.Clear( true );
		_displaySheet.AddRow( Serialized.GetProperty( nameof(ComponentDefinition.Title) ) );
		_displaySheet.AddRow( Serialized.GetProperty( nameof(ComponentDefinition.Description) ) );
		_displaySheet.AddRow( Serialized.GetProperty( nameof(ComponentDefinition.Group) ) );
		_displaySheet.AddRow( Serialized.GetProperty( nameof(ComponentDefinition.Icon) ) );

		_propertyList.Initialize( Serialized.GetProperty( nameof(ComponentDefinition.Properties) ) );
		_methodList.Initialize( Serialized.GetProperty( nameof(ComponentDefinition.Methods) ) );
		_eventList.Initialize( Serialized.GetProperty( nameof(ComponentDefinition.Events) ) );

		Serialized.OnPropertyChanged += NoteChanged;
	}

	protected override void SavedToDisk()
	{
		Resource.Build();

		_lastBuildNumber = Resource.BuildNumber;
	}

	[EditorEvent.Frame]
	private void Frame()
	{
		if ( _lastBuildNumber != Resource.BuildNumber )
		{
			Initialize( Asset, Resource );
		}
	}

	private (Widget Widget, ExpandGroup Group) AddGroup( string title, string icon )
	{
		var group = new ExpandGroup( this );

		group.StateCookieName = $"{nameof( ComponentDefinitionEditor )}.{title}";
		group.Title = title;
		group.Icon = icon;

		Layout.Add( group );

		var widget = new Widget();

		group.SetWidget( widget );

		return (widget, group);
	}

	public void SelectProperty( ComponentPropertyDefinition property )
	{
		_propertiesExpandGroup.SetOpenState( true );
		_propertiesExpandGroup.SetHeight();

		_propertyList.SelectProperty( property );
	}

	public void SelectMethod( ComponentMethodDefinition method )
	{
		_methodsExpandGroup.SetOpenState( true );
		_methodsExpandGroup.SetHeight();

		_methodList.SelectMethod( method );
	}

	public void SelectEvent( ComponentEventDefinition evnt )
	{
		_eventsExpandGroup.SetOpenState( true );
		_eventsExpandGroup.SetHeight();

		_eventList.SelectEvent( evnt );
	}

	public override void SelectMember( string name )
	{
		var property = Resource.Properties.FirstOrDefault( x => x.Name == name );

		if ( property != null )
		{
			SelectProperty( property );
			return;
		}

		var method = Resource.Methods.FirstOrDefault( x => x.Name == name );

		if ( method != null )
		{
			SelectMethod( method );
			return;
		}

		var evnt = Resource.Events.FirstOrDefault( x => x.Name == name );

		if ( evnt != null )
		{
			SelectEvent( evnt );
			return;
		}
	}
}
