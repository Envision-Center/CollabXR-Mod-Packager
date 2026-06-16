using System;
using UnityEngine;

namespace CollabXR.ModPackager
{
	/// <summary>
	/// Generic scriptable data used to circumvent unity's limitation of creating
	/// a scriptable object at run time in a package directory.
	/// <seealso cref="ScriptableDataCreator"/>
	/// </summary>
	[Serializable]
	public class ScriptableData<T> : ScriptableObject
	{
		public T data;

		/// <summary>
		/// Returns an empty scriptable object with a data field of the given type
		/// </summary>
		public static ScriptableObject GetScriptableObjectOfType(Type t)
		{
			return t switch
			{
				Type _ when t == typeof(string) => CreateInstance<ScriptableString>(),
				Type _ when t == typeof(float) => CreateInstance<ScriptableFloat>(),
				_ => null,
			};
		}
	}

	[Serializable]
	public class ScriptableString : ScriptableData<string> { }

	[Serializable]
	public class ScriptableFloat : ScriptableData<float> { }
}
