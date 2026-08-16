using UnityEngine.Scripting;

// Loaded only via Type.GetType from ZTS.Common — without this, managed stripper
// drops the whole assembly and Player fails with "ZTS backend type not found".
[assembly: AlwaysLinkAssembly]
