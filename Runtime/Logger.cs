using UnityEngine;

namespace CollabXR.ModPackager
{
	public class Logger
	{
		private static bool enableVerbose = false;
		private const string DEBUG_LOG_HEADER = "<color=#a557ff>[CollabXR Mod Packager]</color>";

		public static void Info(object message)
		{
			Debug.Log($"{DEBUG_LOG_HEADER} {message.ToString()}");
		}

		public static void Error(object message)
		{
			Debug.LogError($"{DEBUG_LOG_HEADER} {message.ToString()}");
		}

		public static void VerboseInfo(object message)
		{
			if (enableVerbose)
			{
				Debug.Log($"{DEBUG_LOG_HEADER} {message.ToString()}");
			}
		}

		public static void VerboseError(object message)
		{
			if (enableVerbose)
			{
				Debug.LogError($"{DEBUG_LOG_HEADER} {message.ToString()}");
			}
		}

		public static void EnableVerbose(bool enableVerbose)
		{
			Logger.enableVerbose = enableVerbose;

			Logger.VerboseInfo($"Verbose Logging {(enableVerbose ? "Enabled" : "Disabled")}");
		}
	}
}
