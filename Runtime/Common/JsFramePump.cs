// Copyright 2026 Code Philosophy
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Collections;
using UnityEngine;

namespace ZenTS
{
    /// <summary>
    /// Main-thread frame pump: pending JS registry unrefs (LateUpdate) and
    /// deferred <see cref="JsAppDomain.Reset"/> (EndOfFrame).
    /// </summary>
    internal sealed class JsFramePump : MonoBehaviour
    {
        private static JsFramePump _instance;
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

            var gameObject = new GameObject("[ZenTS] FramePump");
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(gameObject);
            _instance = gameObject.AddComponent<JsFramePump>();
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
            JsAppDomain.ProcessPendingRefReleases();
        }

        private static IEnumerator EndOfFrameLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                JsAppDomain.FlushPendingReset();
            }
        }
    }
}
