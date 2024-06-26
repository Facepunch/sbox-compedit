
using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Editor.ActionGraphs;

public class ComponentEventList : GridLayout
{
	private readonly ComponentDefinitionEditor _editor;

	public ComponentEventList( ComponentDefinitionEditor editor )
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

		_controls.Clear();

		foreach ( var def in _targetList )
		{
			AddEvent( def );
		}
	}

	private void AddHeader( int x, string title )
	{
		var label = AddCell( x, 0, new Label( title ) );

		label.MinimumHeight = Theme.RowHeight;
		label.Alignment = TextFlag.LeftCenter;
		label.Margin = 4f;
	}

	public void AddEvent( SerializedProperty evnt )
	{
		var so = evnt.TryGetAsObject( out var obj ) ? obj : throw new Exception();
		var cell = 0;
		var row = ++_rows;

		var id = so.GetProperty( nameof(ComponentEventDefinition.Id) ).GetValue<int>();
		var titleControl = AddCell( cell++, row, ControlWidget.Create( so.GetProperty( nameof( ComponentEventDefinition.Title ) ) ), alignment: TextFlag.LeftTop );

		_controls[id] = titleControl;

		AddCell( cell++, row, new ComponentIconButton( "edit_note", "Edit More (TODO)", () => { } ) );
		AddCell( cell++, row, new ComponentIconButton( "delete", "Delete Event", () =>
		{
			if ( _targetList.Remove( evnt ) )
			{
				_editor.Resource.Build();
			}
		} ) );
	}

	public void SelectEvent( ComponentEventDefinition evnt )
	{
		if ( _controls.TryGetValue( evnt.Id, out var control ) )
		{
			control.StartEditing();
		}
	}
}
