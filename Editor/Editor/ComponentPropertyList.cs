
using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Editor.ActionGraphs;

public class ComponentPropertyList : GridLayout
{
	private readonly ComponentDefinitionEditor _editor;

	public ComponentPropertyList( ComponentDefinitionEditor editor )
	{
		_editor = editor;

		Margin = new Sandbox.UI.Margin( 16, 8, 16, 8 );
		HorizontalSpacing = 4;
		VerticalSpacing = 2;
	}

	private int _rows;
	private SerializedProperty _target;
	private SerializedCollection _targetList;

	private readonly Dictionary<int, ControlWidget> _controls = new();

	public void Initialize( SerializedProperty target )
	{
		Clear( true );

		_rows = 0;

		_target = target;
		_targetList = target.TryGetAsObject( out var list ) && list is SerializedCollection collection
			? collection
			: throw new Exception();

		if ( !_targetList.Any() )
		{
			return;
		}

		var cell = 0;

		AddHeader( cell++, "Name" );
		AddHeader( cell++, "Default Value" );
		AddHeader( cell++, "Type" );

		_controls.Clear();

		foreach ( var def in _targetList )
		{
			AddProperty( def );
		}

		SetMinimumColumnWidth( 0, 120 );
		SetMinimumColumnWidth( 0, 200 );
		SetMinimumColumnWidth( 0, 120 );
		SetColumnStretch( 0, 1, 0 );
	}

	private void AddHeader( int x, string title )
	{
		var label = AddCell( x, 0, new Label( title ) );

		label.MinimumHeight = Theme.RowHeight;
		label.Alignment = TextFlag.LeftCenter;
		label.Margin = 4f;
	}

	public void AddProperty( SerializedProperty property )
	{
		var so = property.TryGetAsObject( out var obj ) ? obj : throw new Exception();
		var cell = 0;
		var row = ++_rows;

		var id = so.GetProperty( nameof(ComponentPropertyDefinition.Id) ).GetValue<int>();
		var titleControl = AddCell( cell++, row, ControlWidget.Create( so.GetProperty( nameof( ComponentPropertyDefinition.Title ) ) ), alignment: TextFlag.LeftTop );
		var typeProperty = so.GetProperty( nameof(ComponentPropertyDefinition.Type) );

		titleControl.FixedWidth = 120;

		_controls[id] = titleControl;
		var typedValueCell = cell++;

		Widget typedValueWidget = null;

		var valueProperty = so.GetProperty( nameof(ComponentPropertyDefinition.DefaultValue) );

		void RefreshValueWidget()
		{
			typedValueWidget?.Destroy();

			var typedValueProperty = new TypedSerializedProperty( valueProperty, typeProperty.GetValue<Type>() );

			typedValueWidget = new Widget();
			typedValueWidget.Layout = Layout.Row();
			typedValueWidget.Layout.Add( ControlWidget.Create( typedValueProperty ) );
			typedValueWidget.Layout.AddStretchCell();

			typedValueWidget = AddCell( typedValueCell, row, typedValueWidget );
		}

		RefreshValueWidget();

		var typeControl = AddCell( cell++, row, ControlWidget.Create( typeProperty ) );

		typeControl.FixedWidth = 120;

		typeControl.OnChildValuesChanged += widget =>
		{
			RefreshValueWidget();
		};

		AddCell( cell++, row, new ComponentIconButton( "edit_note", "Edit More (TODO)", () => { } ) );
		AddCell( cell++, row, new ComponentIconButton( "delete", "Delete Property", () =>
		{
			if ( _targetList.Remove( property ) )
			{
				_editor.Resource.Build();
			}
		} ) );
	}

	public void SelectProperty( ComponentPropertyDefinition property )
	{
		if ( _controls.TryGetValue( property.Id, out var control ) )
		{
			control.StartEditing();
		}
	}
}

file class TypedSerializedProperty : SerializedProperty
{
	public SerializedProperty Inner { get; }
	public Type Type { get; }

	private object _placeholderValue;
	private SerializedObject _placeholderObject;

	public TypedSerializedProperty( SerializedProperty inner, Type type )
	{
		Inner = inner;
		Type = type;
	}

	public override SerializedObject Parent => Inner.Parent;

	public override string Name => Inner.Name;
	public override string DisplayName => Inner.DisplayName;
	public override string Description => Inner.Description;
	public override string GroupName => Inner.GroupName;
	public override bool IsEditable => Inner.IsEditable;
	public override Type PropertyType => Type;

	public override string SourceFile => Inner.SourceFile;
	public override int SourceLine => Inner.SourceLine;
	public override bool HasChanges => Inner.HasChanges;

	public override void SetValue<T>( T value ) => Inner.SetValue( value );
	public override T GetValue<T>( T defaultValue = default ) => Inner.GetValue( defaultValue );

	public override IEnumerable<Attribute> GetAttributes() => Inner.GetAttributes();

	public override bool TryGetAsObject( out SerializedObject obj )
	{
		if ( !Type.IsValueType )
		{
			return Inner.TryGetAsObject( out obj );
		}

		if ( _placeholderObject is not null )
		{
			obj = _placeholderObject;
			return true;
		}

		_placeholderValue = GetValue<object>();

		if ( _placeholderValue?.GetType() != PropertyType )
		{
			_placeholderValue = Activator.CreateInstance( Type );
		}

		_placeholderObject = TypeLibrary.GetSerializedObject( _placeholderValue );
		_placeholderObject.ParentProperty = Inner;

		_placeholderObject.OnPropertyChanged += _ =>
		{
			Inner.SetValue( _placeholderValue );
		};

		obj = _placeholderObject;
		return true;
	}

	public override bool IsMultipleValues => Inner.IsMultipleValues;
	public override bool IsMultipleDifferentValues => Inner.IsMultipleDifferentValues;
}
