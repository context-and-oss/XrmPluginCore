using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using XrmPluginCore.SourceGenerator.Helpers;

namespace XrmPluginCore.SourceGenerator.Models;

/// <summary>
/// Represents metadata about a plugin step registration that includes filtered attributes
/// </summary>
internal sealed class PluginStepMetadata
{
	public string EntityTypeName { get; set; }

	/// <summary>
	/// Gets or sets the fully qualified entity type name for typed entity wrapper generation.
	/// </summary>
	public string EntityTypeFullName { get; set; }

	public string EventOperation { get; set; }
	public string ExecutionStage { get; set; }
	public List<ImageMetadata> Images { get; set; } = [];
	public string Namespace { get; set; }
	public string PluginClassName { get; set; }

	/// <summary>
	/// Gets or sets the service type name (short name) for action wrapper generation.
	/// </summary>
	public string ServiceTypeName { get; set; }

	/// <summary>
	/// Gets or sets the fully qualified service type name for action wrapper generation.
	/// </summary>
	public string ServiceTypeFullName { get; set; }

	/// <summary>
	/// Gets or sets the handler method name on the service.
	/// </summary>
	public string HandlerMethodName { get; set; }

	/// <summary>Whether the consuming compilation has nullable reference-type annotations enabled.</summary>
	public bool NullableAnnotationsEnabled { get; set; }

	/// <summary>
	/// Diagnostics to report for this plugin step. Not included in equality comparison.
	/// </summary>
	public List<DiagnosticInfo> Diagnostics { get; set; } = [];

	/// <summary>
	/// The identifier prefix shared by every generated type for this registration, derived from the
	/// registration tuple and sanitized into a valid identifier. Uniqueness within the plugin namespace
	/// is guaranteed by XPC2002 (one registration per entity/operation/stage per plugin) and by distinct
	/// plugin class names. Kept in sync with the runtime discovery in <c>Plugin.RegisterStep</c>.
	/// </summary>
	public string TypePrefix =>
		IdentifierHelper.Sanitize($"{PluginClassName}{EntityTypeName}{EventOperation}{ExecutionStage}");

	/// <summary>The generated wrapper class name for the given image type (e.g. "...PreImage").</summary>
	public string ImageClassName(string imageType) => $"{TypePrefix}{imageType}";

	/// <summary>The generated ActionWrapper class name for this registration.</summary>
	public string ActionWrapperClassName => $"{TypePrefix}{Constants.ActionWrapperClassSuffix}";

	/// <summary>
	/// Gets a unique identifier for this registration.
	/// Includes namespace and plugin class name to differentiate multiple registrations
	/// for the same entity/operation/stage across different namespaces.
	/// </summary>
	public string UniqueId =>
		$"{Namespace?.Replace(".", "_")}_{PluginClassName}_{EntityTypeName}_{EventOperation}_{ExecutionStage}";

	public override bool Equals(object obj)
	{
		if (obj is PluginStepMetadata other)
		{
			return PluginClassName == other.PluginClassName
				&& EntityTypeName == other.EntityTypeName
				&& EntityTypeFullName == other.EntityTypeFullName
				&& EventOperation == other.EventOperation
				&& ExecutionStage == other.ExecutionStage
				&& Images.SequenceEqual(other.Images)
				&& Namespace == other.Namespace
				&& ServiceTypeName == other.ServiceTypeName
				&& ServiceTypeFullName == other.ServiceTypeFullName
				&& HandlerMethodName == other.HandlerMethodName
				&& NullableAnnotationsEnabled == other.NullableAnnotationsEnabled;
		}
		return false;
	}

	public override int GetHashCode()
	{
		unchecked
		{
			var hash = 17;
			hash = (hash * 31) + (PluginClassName?.GetHashCode() ?? 0);
			hash = (hash * 31) + (EntityTypeName?.GetHashCode() ?? 0);
			hash = (hash * 31) + (EntityTypeFullName?.GetHashCode() ?? 0);
			hash = (hash * 31) + (EventOperation?.GetHashCode() ?? 0);
			hash = (hash * 31) + (ExecutionStage?.GetHashCode() ?? 0);
			hash = (hash * 31) + (Namespace?.GetHashCode() ?? 0);
			hash = (hash * 31) + (ServiceTypeName?.GetHashCode() ?? 0);
			hash = (hash * 31) + (ServiceTypeFullName?.GetHashCode() ?? 0);
			hash = (hash * 31) + (HandlerMethodName?.GetHashCode() ?? 0);
			hash = (hash * 31) + NullableAnnotationsEnabled.GetHashCode();
			foreach (var img in Images)
			{
				hash = (hash * 31) + img.GetHashCode();
			}
			return hash;
		}
	}
}

/// <summary>
/// Represents metadata about an entity attribute
/// </summary>
internal sealed class AttributeMetadata
{
	public string PropertyName { get; set; }
	public string LogicalName { get; set; }
	public string TypeName { get; set; }

	/// <summary>
	/// Gets or sets the XML documentation from the underlying entity property.
	/// </summary>
	public string XmlDocumentation { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the underlying entity property is marked
	/// with <see cref="System.ObsoleteAttribute"/>. When true, the generated wrapper property
	/// is also marked obsolete so the deprecation warning surfaces in the calling code rather
	/// than inside the auto-generated image class.
	/// </summary>
	public bool IsObsolete { get; set; }

	/// <summary>
	/// Gets or sets the message supplied to the underlying property's <see cref="System.ObsoleteAttribute"/>, if any.
	/// </summary>
	public string ObsoleteMessage { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the underlying property's <see cref="System.ObsoleteAttribute"/>
	/// treats usage as an error (the second constructor argument).
	/// </summary>
	public bool ObsoleteIsError { get; set; }

	public override bool Equals(object obj)
	{
		if (obj is AttributeMetadata other)
		{
			return PropertyName == other.PropertyName
				&& LogicalName == other.LogicalName
				&& TypeName == other.TypeName
				&& XmlDocumentation == other.XmlDocumentation
				&& IsObsolete == other.IsObsolete
				&& ObsoleteMessage == other.ObsoleteMessage
				&& ObsoleteIsError == other.ObsoleteIsError;
		}
		return false;
	}

	public override int GetHashCode()
	{
		unchecked
		{
			var hash = 17;
			hash = (hash * 31) + (PropertyName?.GetHashCode() ?? 0);
			hash = (hash * 31) + (LogicalName?.GetHashCode() ?? 0);
			hash = (hash * 31) + (TypeName?.GetHashCode() ?? 0);
			hash = (hash * 31) + (XmlDocumentation?.GetHashCode() ?? 0);
			hash = (hash * 31) + IsObsolete.GetHashCode();
			hash = (hash * 31) + (ObsoleteMessage?.GetHashCode() ?? 0);
			hash = (hash * 31) + ObsoleteIsError.GetHashCode();
			return hash;
		}
	}
}

/// <summary>
/// Represents metadata about a plugin step image (PreImage or PostImage)
/// </summary>
internal sealed class ImageMetadata
{
	public string ImageType { get; set; } // "PreImage" or "PostImage"
	public string ImageName { get; set; }
	public List<AttributeMetadata> Attributes { get; set; } = [];

	public override bool Equals(object obj)
	{
		if (obj is ImageMetadata other)
		{
			return ImageType == other.ImageType
				&& ImageName == other.ImageName
				&& Attributes.SequenceEqual(other.Attributes);
		}
		return false;
	}

	public override int GetHashCode()
	{
		unchecked
		{
			var hash = 17;
			hash = (hash * 31) + (ImageType?.GetHashCode() ?? 0);
			hash = (hash * 31) + (ImageName?.GetHashCode() ?? 0);
			foreach (var attr in Attributes)
			{
				hash = (hash * 31) + attr.GetHashCode();
			}
			return hash;
		}
	}
}

/// <summary>
/// Represents a diagnostic to be reported during source generation.
/// Note: Location is not stored to avoid caching stale SyntaxTree references across incremental compilations.
/// </summary>
internal sealed class DiagnosticInfo
{
	public DiagnosticDescriptor Descriptor { get; set; }
	public object[] MessageArgs { get; set; }
}
