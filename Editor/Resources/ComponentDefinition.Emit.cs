using System;
using System.Text.Json.Serialization;

namespace Sandbox;

#nullable enable

partial class ComponentDefinition
{
	public static int BuildNumber { get; private set; } = 100;

	private Type? _type;
	private int _lastBuiltHash;

	[Hide, JsonIgnore]
	public Type GeneratedType
	{
		get
		{
			if ( _type is not null ) return _type;

			Build();
			return _type!;
		}
	}

	protected override void PostLoad()
	{
		UpdateMembers();

		if ( _type is null || _lastBuiltHash != GetDefinitionHash() )
		{
			Build();
		}
	}

	protected override void PostReload()
	{
		UpdateMembers();

		if ( _type is null || _lastBuiltHash != GetDefinitionHash() )
		{
			Build();
		}
	}

	/// <summary>
	/// A hash of this type's definition. If it's changed we need to rebuild.
	/// </summary>
	internal int GetDefinitionHash()
	{
		var hashCode = new HashCode();

		foreach ( var propertyDef in Properties )
		{
			hashCode.Add( propertyDef.GetDefinitionHash() );
		}

		foreach ( var method in Methods)
		{
			hashCode.Add( method.GetDefinitionHash() );
		}

		foreach ( var evnt in Events )
		{
			hashCode.Add( evnt.GetDefinitionHash() );
		}

		return hashCode.ToHashCode();
	}

	public static void RebuildAll()
	{
		Build();
	}

	private static bool _isBuilding;

	private static void Build()
	{

	}
}

partial class ComponentPropertyDefinition
{
	internal int GetDefinitionHash()
	{
		var hashCode = new HashCode();

		hashCode.Add( Name );
		hashCode.Add( Access );
		hashCode.Add( Type.FullName );

		return hashCode.ToHashCode();
	}
}

partial class ComponentMethodDefinition
{
	internal int GetDefinitionHash()
	{
		var hashCode = new HashCode();

		hashCode.Add( Name );
		hashCode.Add( Override );
		hashCode.Add( Access );

		if ( !Override )
		{
			var binding = GetBinding( true );

			foreach ( var inputDef in binding.Inputs )
			{
				if ( inputDef.IsTarget )
				{
					continue;
				}

				hashCode.Add( inputDef.Name );
				hashCode.Add( inputDef.Type.FullName );
			}

			foreach ( var outputDef in binding.Outputs )
			{
				hashCode.Add( outputDef.Name );
				hashCode.Add( outputDef.Type.FullName );
			}
		}

		return hashCode.ToHashCode();
	}
}

partial class ComponentEventDefinition
{
	internal int GetDefinitionHash()
	{
		var hashCode = new HashCode();

		hashCode.Add( Name );

		foreach ( var inputDef in Inputs )
		{
			if ( inputDef.IsTarget )
			{
				continue;
			}

			hashCode.Add( inputDef.Name );
			hashCode.Add( inputDef.Type.FullName );
		}

		return hashCode.ToHashCode();
	}
}

file static class Helpers
{

}
