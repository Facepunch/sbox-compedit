
using Editor;
using Sandbox;
using System.Linq;
using System;

namespace Editor.ActionGraphs;

public class ComponentIconButton : IconButton
{
	public ComponentIconButton( string icon, string toolTip, Action onClick = null )
		: base( icon, onClick )
	{
		Background = Color.Transparent;
		IconSize = Theme.RowHeight - 2f;
		FixedSize = Theme.RowHeight;
		ToolTip = toolTip;
	}
}

public class ComponentToggleIconButton : ComponentIconButton
{
	private const string DefaultIcon = "question_mark";

	public SerializedProperty Property { get; }

	public string TrueIcon { get; }
	public string FalseIcon { get; }

	public ComponentToggleIconButton( SerializedProperty property )
		: base( "", property.Description ?? property.DisplayName ?? property.Name )
	{
		Property = property;

		UpdateEnumIcon();
	}

	public ComponentToggleIconButton( SerializedProperty property, string falseIcon, string trueIcon )
		: base( property.GetValue<bool>() ? trueIcon : falseIcon, property.Description ?? property.DisplayName ?? property.Name )
	{
		Property = property;
		TrueIcon = trueIcon;
		FalseIcon = falseIcon;
	}

	private void UpdateEnumIcon()
	{
		var enumDesc = EditorTypeLibrary.GetEnumDescription( Property.PropertyType );

		var entry = enumDesc.GetEntry( Property.GetValue( 0L ) );

		Icon = entry.Icon ?? DefaultIcon;
		ToolTip = entry.Description ?? Property.Description ?? Property.DisplayName ?? Property.Name;
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( Property.PropertyType == typeof( bool ) )
		{
			var value = !Property.GetValue<bool>();

			Property.SetValue( value );
			Icon = value ? TrueIcon : FalseIcon;

			SignalValuesChanged();

			return;
		}

		if ( Property.PropertyType.IsEnum )
		{
			var enumDesc = EditorTypeLibrary.GetEnumDescription( Property.PropertyType );
			var entries = enumDesc.ToArray();
			var entry = enumDesc.GetEntry( Property.GetValue( 0L ) );

			var index = Array.IndexOf( entries, entry );

			entry = entries[(index + 1) % entries.Length];

			Property.SetValue( entry.ObjectValue );

			UpdateEnumIcon();

			SignalValuesChanged();
		}
	}
}
