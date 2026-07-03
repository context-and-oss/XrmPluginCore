using System;

namespace XrmPluginCore.Tests.TestPlugins.TypeSafe;

/// <summary>
/// Service for TypeSafeContactPlugin that receives PreImage directly. The image wrapper type is
/// generated into this namespace, named after the registration.
/// </summary>
public class TypeSafeContactService
{
	private readonly TypeSafeContactPlugin plugin;

	public TypeSafeContactService(TypeSafeContactPlugin plugin)
	{
		this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
	}

	public void HandleCreate(TypeSafeContactPluginContactCreatePostOperationPreImage preImage)
	{
		if (preImage != null)
		{
			_ = preImage.Firstname;
			_ = preImage.Lastname;
			_ = preImage.Mobilephone;
		}

		plugin.SetExecutionResult(preImage);
	}
}
