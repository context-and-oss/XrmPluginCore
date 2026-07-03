using System;

namespace XrmPluginCore.Tests.TestPlugins.TypeSafe;

/// <summary>
/// Service for TypeSafeAccountPlugin that receives images directly. The image wrapper types are
/// generated into this namespace, named after the registration.
/// </summary>
public class TypeSafeAccountService(TypeSafeAccountPlugin plugin)
{
	private readonly TypeSafeAccountPlugin plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));

	public void HandleUpdate(TypeSafeAccountPluginAccountUpdatePreOperationPreImage preImage, TypeSafeAccountPluginAccountUpdatePreOperationPostImage postImage)
	{
		if (preImage != null)
		{
			_ = preImage.Name;
			_ = preImage.AccountNumber;
			_ = preImage.Revenue;
		}

		plugin.SetExecutionResult(preImage, postImage);
	}
}
