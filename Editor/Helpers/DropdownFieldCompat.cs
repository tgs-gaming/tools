#if !UNITY_2021_2_OR_NEWER
using System.Collections.Generic;

// DropdownField was added in Unity 2021.2. This no-op shim registers it as a
// known UXML factory so UXML files referencing <ui:DropdownField> can be
// instantiated without a "no registered factory method" crash on 2020.3.
// All real DropdownField behaviour is guarded with #if UNITY_2021_2_OR_NEWER in C#.
namespace UnityEngine.UIElements
{
    internal class DropdownField : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<DropdownField, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlStringAttributeDescription _choices = new UxmlStringAttributeDescription { name = "choices" };
            private readonly UxmlIntAttributeDescription _index = new UxmlIntAttributeDescription { name = "index", defaultValue = 0 };
            private readonly UxmlStringAttributeDescription _label = new UxmlStringAttributeDescription { name = "label" };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
            }
        }

        public List<string> choices { get; set; } = new List<string>();
        public int index { get; set; }
    }
}
#endif
