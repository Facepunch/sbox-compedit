using Sandbox.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

#nullable enable

[GameResource( "Component Definition", "comp",
	"Describes the properties, methods and events of a component type.",
	Icon = "article" )]
public class ComponentResource : GameResource
{
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

	public List<PropertyModel> Properties { get; set; } = new();
	public List<MethodModel> Methods { get; set; } = new();
	public List<EventModel> Events { get; set; } = new();

	private Type? _type;

	[Hide, JsonIgnore] public Type? GeneratedType => _type ??= GlobalGameNamespace.TypeLibrary.GetType( ResourcePath )?.TargetType;

	[JsonIgnore, Hide]
	protected override object? ActionGraphTarget => null;

	[JsonIgnore, Hide]
	protected override Type ActionGraphTargetType => GeneratedType!;

	public record PropertyModel( int Id, Type Type, JsonNode? Default = null,
		PropertyAccess Access = PropertyAccess.Public, bool InitOnly = false,
		string? Title = null, string? Description = null, string? Group = null, string? Icon = null, bool Hide = false );

	[JsonPolymorphic]
	[JsonDerivedType( typeof( OverrideMethodModel ), "Override" )]
	[JsonDerivedType( typeof( NewMethodModel ), "New" )]
	public record MethodModel( JsonNode? Body );

	public record OverrideMethodModel( string Name, JsonNode? Body = null ) : MethodModel( Body );
	public record NewMethodModel( int Id, MethodAccess Access = MethodAccess.Public, JsonNode? Body = null ) : MethodModel( Body );

	public record EventModel( int Id, IReadOnlyList<JsonNode> Inputs,
		string? Title = null, string? Description = null, string? Group = null, string? Icon = null );

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

	public IDisposable PushSerializationScopeInternal()
	{
		return PushSerializationScope();
	}

	public T? GetMethodBody<T>( int methodId )
		where T : Delegate
	{
		using var _ = PushSerializationScope();

		return Json.FromNode<T>( Methods.OfType<NewMethodModel>().FirstOrDefault( x => x.Id == methodId )?.Body );
	}

	public T? GetMethodBody<T>( string methodName )
		where T : Delegate
	{
		using var _ = PushSerializationScope();

		return Json.FromNode<T>( Methods.OfType<OverrideMethodModel>().FirstOrDefault( x => x.Name == methodName )?.Body );
	}
}
