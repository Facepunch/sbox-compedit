
using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch.ActionGraphs;

namespace Editor.ActionGraphs;

public class ComponentMethodList : GridLayout
{
	private readonly ComponentDefinitionEditor _editor;

	public ComponentMethodList( ComponentDefinitionEditor editor )
	{
		_editor = editor;

		Margin = new Sandbox.UI.Margin( 16, 8, 16, 8 );
		HorizontalSpacing = 4;
		VerticalSpacing = 2;

		SetMinimumColumnWidth( 0, 120 );
		SetColumnStretch( 1f, 0f, 0f );
	}

	private int _rows;
	private SerializedProperty _target;
	private SerializedCollection _targetList;

	private readonly Dictionary<string, ControlWidget> _controls = new();

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

		_controls.Clear();

		var groups = _targetList.GroupBy( x =>
		{
			var value = x.GetValue<ComponentMethodDefinition>();
			return value.Override ? "Overrides" : value.Display.Group ?? $"{value.GetBinding().Kind}s";
		} );

		groups = groups.OrderBy( x => x.Key );

		foreach ( var group in groups )
		{
			if ( group.Key != null )
			{
				var label = AddCell( 0, _rows++, new Label( group.Key ) );

				label.MinimumHeight = Theme.RowHeight;
				label.Alignment = TextFlag.LeftCenter;
				label.Margin = 4f;
			}

			foreach ( var def in group )
			{
				AddMethod( def );
			}
		}
	}

	public void AddMethod( SerializedProperty property )
	{
		var so = property.TryGetAsObject( out var obj ) ? obj : throw new Exception();
		var cell = 0;
		var row = _rows++;

		var name = so.GetProperty( nameof( ComponentMethodDefinition.Name ) ).GetValue<string>();
		var isOverride = so.GetProperty( nameof(ComponentMethodDefinition.Override) ).GetValue<bool>();

		var body = AddCell( cell++, row, ControlWidget.Create( so.GetProperty( nameof( ComponentMethodDefinition.Body ) ) ) );

		if ( body is not ActionControlWidget )
		{
			Log.Warning( "Expected a ActionControlWidget" );
		}
		else
		{
			_controls[name] = body;
		}

		AddCell( cell++, row, new ComponentIconButton( "edit_note", "Edit More (TODO)", () => { } ) );
		AddCell( cell++, row, new ComponentIconButton( "delete", "Delete Method", () =>
		{
			if ( _targetList.Remove( property ) )
			{
				_editor.Resource.Build();
			}
		} ) );
	}

	public void SelectMethod( ComponentMethodDefinition method )
	{
		if ( _controls.TryGetValue( method.Name, out var control ) )
		{
			control.StartEditing();
		}
	}
}
