using UnityEngine;
namespace OptifunityValidation
{
    public class ValidationGameplay : MonoBehaviour
    {
        void Update()
        {
            var value = new CustomValue();
            object boxed = value;
            var reference = new CustomReference();
            var array = new int[4];
#if UNITY_EDITOR
            var editorArray = new float[4];
#endif
#if OPTIFUNITY_UNDEFINED
            var excluded = new byte[4];
#endif
        }
    }
}
