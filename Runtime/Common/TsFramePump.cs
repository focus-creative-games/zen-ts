using System.Collections;
using UnityEngine;

namespace ZTS
{
    /// <summary>
    /// Main-thread frame pump: pending JS registry unrefs (LateUpdate) and
    /// deferred <see cref="TsAppDomain.Reset"/> (EndOfFrame).
    /// </summary>
    internal sealed class TsFramePump : MonoBehaviour
    {
        private static TsFramePump _instance;
        private Coroutine _endOfFrameRoutine;

        internal static void EnsureRegistered()
        {
            if (_instance != null)
            {
                return;
            }

#if UNITY_EDITOR
            // DontDestroyOnLoad is play-mode only; edit-mode / batch -executeMethod skip the GO pump.
            if (!Application.isPlaying)
            {
                return;
            }
#endif

            var gameObject = new GameObject("[ZTS] FramePump");
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(gameObject);
            _instance = gameObject.AddComponent<TsFramePump>();
        }

        internal static void Unregister()
        {
            if (_instance == null)
            {
                return;
            }

            GameObject gameObject = _instance.gameObject;
            _instance = null;
            if (gameObject != null)
            {
                Object.Destroy(gameObject);
            }
        }

        private void OnEnable()
        {
            if (_endOfFrameRoutine == null)
            {
                _endOfFrameRoutine = StartCoroutine(EndOfFrameLoop());
            }
        }

        private void OnDisable()
        {
            if (_endOfFrameRoutine != null)
            {
                StopCoroutine(_endOfFrameRoutine);
                _endOfFrameRoutine = null;
            }
        }

        private void LateUpdate()
        {
            TsAppDomain.ProcessPendingRefReleases();
        }

        private static IEnumerator EndOfFrameLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                TsAppDomain.FlushPendingReset();
            }
        }
    }
}
