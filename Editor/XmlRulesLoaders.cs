using System.Xml.Linq;

namespace ZTS.Editor
{
    internal static class AliasRulesLoader
    {
        public static XDocument LoadOrEmpty(string path)
        {
            try
            {
                return string.IsNullOrEmpty(path) ? new XDocument() : XDocument.Load(path);
            }
            catch
            {
                return new XDocument();
            }
        }
    }

    internal static class MarshalAsRulesLoader
    {
        public static XDocument LoadOrEmpty(string path)
        {
            try
            {
                return string.IsNullOrEmpty(path) ? new XDocument() : XDocument.Load(path);
            }
            catch
            {
                return new XDocument();
            }
        }
    }

    internal static class ExtensionRulesLoader
    {
        public static XDocument LoadOrEmpty(string path)
        {
            try
            {
                return string.IsNullOrEmpty(path) ? new XDocument() : XDocument.Load(path);
            }
            catch
            {
                return new XDocument();
            }
        }
    }
}
