﻿using System.IO;
using Sandbox;

namespace Editor.ActionGraphs;

[Icon( "account_tree" )]
[Title( "Action Graph Component" )]
[Description( "A component with methods you build in Action Graph." )]
public class ActionGraphComponentTemplate : ComponentTemplate
{
	public override string NameFilter => "Component Definition (*.comp)";
	public override string Suffix => ".comp";
	public override string DefaultDirectory => Path.Combine( Project.Current.GetAssetsPath(), "Components" );

	public override void Create( string componentName, string path )
	{
		var asset = AssetSystem.CreateResource( "comp", path );

		var resource = new ComponentResource
		{
			Title = componentName.ToTitleCase(),
			Group = "Custom"
		};

		asset.SaveToDisk( resource );

		var def = ComponentDefinition.Get( resource );

		def.Build();
	}
}
