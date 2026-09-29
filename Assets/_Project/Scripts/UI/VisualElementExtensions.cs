using UnityEngine;
using UnityEngine.UIElements;

namespace WindFarm.UI
{
    internal static class VisualElementExtensions
    {
        /// <summary>
        /// Like <c>Q&lt;T&gt;(name)</c>, but logs which element is missing. A renamed element in the UXML otherwise
        /// only shows up later as a NullReferenceException far from the cause.
        /// </summary>
        public static T Require<T>(this VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null)
                Debug.LogError($"Dashboard: element '{name}' ({typeof(T).Name}) not found in the UXML.");
            return element;
        }
    }
}
