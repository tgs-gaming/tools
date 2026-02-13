using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace com.tgs.packagemanager.editor
{
    internal static class EditorCoroutineRunner
    {
        private class CoroutineState
        {
            public IEnumerator Routine;
            public object Current;
        }

        private static readonly List<CoroutineState> Routines = new List<CoroutineState>();
        private static bool _isHooked;

        public static void StartCoroutine(IEnumerator routine)
        {
            if (routine == null)
            {
                return;
            }

            Routines.Add(new CoroutineState { Routine = routine, Current = null });
            if (!_isHooked)
            {
                _isHooked = true;
                EditorApplication.update += Update;
            }
        }

        private static void Update()
        {
            for (var i = Routines.Count - 1; i >= 0; i--)
            {
                var state = Routines[i];
                if (!MoveNext(state))
                {
                    Routines.RemoveAt(i);
                }
            }

            if (Routines.Count == 0 && _isHooked)
            {
                _isHooked = false;
                EditorApplication.update -= Update;
            }
        }

        private static bool MoveNext(CoroutineState state)
        {
            if (state.Current is UnityWebRequestAsyncOperation webOp)
            {
                if (!webOp.isDone)
                {
                    return true;
                }

                state.Current = null;
            }
            else if (state.Current is AsyncOperation asyncOp)
            {
                if (!asyncOp.isDone)
                {
                    return true;
                }

                state.Current = null;
            }
            else if (state.Current is EditorWaitForSeconds wait)
            {
                if (!wait.IsDone)
                {
                    return true;
                }

                state.Current = null;
            }

            if (!state.Routine.MoveNext())
            {
                return false;
            }

            state.Current = state.Routine.Current;
            return true;
        }

        internal class EditorWaitForSeconds
        {
            private readonly double _endTime;

            public EditorWaitForSeconds(float seconds)
            {
                _endTime = EditorApplication.timeSinceStartup + seconds;
            }

            public bool IsDone => EditorApplication.timeSinceStartup >= _endTime;
        }
    }
}
