﻿/*
 * Copyright 2020 Peter Han
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software
 * and associated documentation files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or
 * substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
 * BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
 * DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;

namespace PeterHan.DebugNotIncluded {
	/// <summary>
	/// Provides log functions for this mod.
	/// </summary>
	public static class DebugLogger {
		/// <summary>
		/// The header prepended to each log message from this mod.
		/// </summary>
		public const string HEADER = "[DebugNotIncluded] ";

		/// <summary>
		/// The handler for Unity exception messages.
		/// </summary>
		internal static WrapLogHandler Handler { get; private set; }

		/// <summary>
		/// Logs the exception using the default handler.
		/// </summary>
		/// <param name="e">The exception thrown.</param>
		/// <param name="context">The context of the exception.</param>
		internal static void BaseLogException(Exception e, UnityEngine.Object context) {
			if (Handler == null)
				UnityEngine.Debug.LogException(e, context);
			else
				Handler.Wrapped.LogException(e, context);
		}

		/// <summary>
		/// Outputs information about what patched the method to the output log.
		/// </summary>
		/// <param name="method">The method to check.</param>
		public static void DumpPatchInfo(MethodBase method) {
			if (method == null)
				throw new ArgumentNullException("method");
			var message = new StringBuilder(256);
			// List patches for that method
			message.Append("Patches for ");
			message.Append(method.ToString());
			message.AppendLine(":");
			DebugUtils.GetPatchInfo(method, message);
			LogDebug(message.ToString());
		}

		/// <summary>
		/// Gets the log message for the specified exception.
		/// </summary>
		/// <param name="e">The exception, which must not be null.</param>
		/// <param name="cache">The cache of Harmony methods.</param>
		/// <returns>The log message for this exception.</returns>
		private static string GetExceptionLog(Exception e, HarmonyMethodCache cache) {
			// Better breakdown of the stack trace
			var message = new StringBuilder(8192);
			var stackTrace = new StackTrace(e);
			message.AppendFormat("{0}: {1}", e.GetType().Name, e.Message ?? "<no message>");
			message.AppendLine();
			GetStackTraceLog(stackTrace, cache, message);
			// Log the root cause
			var cause = e.GetBaseException();
			if (cause != null && cause != e) {
				message.AppendLine("Root cause exception:");
				message.Append(GetExceptionLog(cause, cache));
			}
			return message.ToString();
		}

		/// <summary>
		/// Gets the log message for the specified stack trace.
		/// </summary>
		/// <param name="stackTrace">The stack trace, which must not be null.</param>
		/// <param name="cache">The cache of Harmony methods.</param>
		/// <param name="message">The location where the message will be stored.</param>
		internal static void GetStackTraceLog(StackTrace stackTrace, HarmonyMethodCache cache,
				StringBuilder message) {
			var registry = ModDebugRegistry.Instance;
			ModDebugInfo mod;
			int n = stackTrace.FrameCount;
			for (int i = 0; i < n; i++) {
				var frame = stackTrace.GetFrame(i);
				var method = frame?.GetMethod();
				if (method == null)
					// Try to output as much as possible
					method = cache.ParseInternalName(frame, message);
				if (method != null) {
					// Try to give as much debug info as possible
					int line = frame.GetFileLineNumber(), chr = frame.GetFileColumnNumber();
					message.Append("  at ");
					DebugUtils.AppendMethod(message, method);
					if (line > 0 || chr > 0)
						message.AppendFormat(" ({0:D}, {1:D})", line, chr);
					else
						message.AppendFormat(" [{0:D}]", frame.GetILOffset());
					// The blame game
					var type = method.DeclaringType;
					if (type.IsBaseGameType())
						message.Append(" <Klei>");
					else if (type.Assembly == typeof(string).Assembly) {
						message.Append(" <mscorlib>");
					} else if ((mod = registry.OwnerOfType(type)) != null) {
						message.Append(" <");
						message.Append(mod.ModName ?? "unknown");
						message.Append(">");
					}
					// If the method shows up, then it was not a patched method, but never
					// hurts to try anyways
					message.AppendLine();
					DebugUtils.GetPatchInfo(method, message);
				}
			}
		}

		/// <summary>
		/// Wraps the default debug log handler, providing better debug support of exceptions.
		/// </summary>
		internal static void InstallExceptionLogger() {
			var logger = UnityEngine.Debug.unityLogger;
			if (logger != null) {
				logger.logHandler = Handler = new WrapLogHandler(logger.logHandler);
#if DEBUG
				LogDebug("Installed exception handler for Debug.LogException");
#endif
			} else
				Handler = null;
		}

		/// <summary>
		/// Logs a debug message.
		/// </summary>
		/// <param name="message">The message to log.</param>
		public static void LogDebug(string message) {
			Debug.Log(HEADER + message);
		}

		/// <summary>
		/// Logs a debug message with format arguments.
		/// </summary>
		/// <param name="message">The message to log.</param>
		/// <param name="args">The format string arguments,</param>
		public static void LogDebug(string message, params object[] args) {
			Debug.LogFormat(HEADER + message, args);
		}

		/// <summary>
		/// Logs an error message.
		/// </summary>
		/// <param name="message">The message to log.</param>
		public static void LogError(string message) {
			// Avoid duplicate messages by replicating the Debug log statement
			UnityEngine.Debug.LogErrorFormat("[{0:HH:mm:ss.fff}] [{1:D}] [ERROR] {2}{3}",
				System.DateTime.UtcNow, Thread.CurrentThread.ManagedThreadId, HEADER, message);
		}

		/// <summary>
		/// Logs an error message with format arguments.
		/// </summary>
		/// <param name="message">The message to log.</param>
		/// <param name="args">The format string arguments,</param>
		public static void LogError(string message, params object[] args) {
			LogError(string.Format(message, args));
		}

		/// <summary>
		/// Logs an exception with a detailed breakdown.
		/// </summary>
		/// <param name="e">The exception to log.</param>
		public static void LogException(Exception e) {
			// Unwrap target invocation exceptions
			while (e is TargetInvocationException tie)
				e = tie.InnerException;
			if (e == null)
				LogError("<null>");
			else
				try {
					LogError(GetExceptionLog(e, new HarmonyMethodCache()));
				} catch {
					// Ensure it gets logged at all costs
					BaseLogException(e, null);
					throw;
				}
		}

		/// <summary>
		/// Logs an exception with a detailed breakdown. This overload is used in KMonoBehavior
		/// transpilers.
		/// </summary>
		/// <param name="e">The exception to log.</param>
		internal static void LogKMonoException(Exception e) {
			var cause = e.InnerException ?? e;
			try {
				LogError((e.Message ?? "Error in KMonoBehaviour:") + Environment.NewLine +
					GetExceptionLog(cause, new HarmonyMethodCache()));
			} catch {
				// Ensure it gets logged at all costs
				BaseLogException(cause, null);
				throw;
			}
		}

		/// <summary>
		/// Logs a warning message.
		/// </summary>
		/// <param name="message">The message to log.</param>
		public static void LogWarning(string message) {
			Debug.LogWarning(HEADER + message);
		}

		/// <summary>
		/// Logs a warning message with format arguments.
		/// </summary>
		/// <param name="message">The message to log.</param>
		/// <param name="args">The format string arguments,</param>
		public static void LogWarning(string message, params object[] args) {
			Debug.LogWarningFormat(HEADER + message, args);
		}

		/// <summary>
		/// Logs a failed assertion that is about to occur.
		/// </summary>
		internal static void OnAssertFailed(bool condition) {
			if (!condition) {
				var message = new StringBuilder(1024);
				message.AppendLine("An assert is about to fail:");
				// Better stack traces!
				GetStackTraceLog(new StackTrace(2), new HarmonyMethodCache(), message);
				LogError(message.ToString());
				message.Clear();
			}
		}
	}
}