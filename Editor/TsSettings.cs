using UnityEngine;

namespace ZTS.Editor
{
    /// <summary>
    /// Editor settings stub for ZTS package configuration.
    /// </summary>
    public sealed class TsSettings : ScriptableObject
    {
        [SerializeField] private bool enableJsPrintBuffer = true;
        [SerializeField] private string aliasRulesPath = "Assets/CustomTsAliasRules.xml";
        [SerializeField] private string marshalAsRulesPath = "Assets/CustomTsMarshalAsRules.xml";
        [SerializeField] private string extensionRulesPath = "Assets/CustomTsExtensionRules.xml";

        public bool EnableJsPrintBuffer => enableJsPrintBuffer;
        public string AliasRulesPath => aliasRulesPath;
        public string MarshalAsRulesPath => marshalAsRulesPath;
        public string ExtensionRulesPath => extensionRulesPath;
    }
}
