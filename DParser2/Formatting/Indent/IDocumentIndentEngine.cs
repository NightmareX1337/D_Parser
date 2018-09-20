﻿//
// IDocumentIndentEngine.cs
//
// Author:
//       Matej Miklečić <matej.miklecic@gmail.com>
//
// Copyright (c) 2013 Matej Miklečić (matej.miklecic@gmail.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;

namespace D_Parser.Formatting.Indent
{
	/// <summary>
	///     The base interface for all indent engines.
	/// </summary>
	interface IDocumentIndentEngine : ICloneable
	{
		/// <summary>
		///     The indentation string of the current line.
		/// </summary>
		string ThisLineIndent { get; }

		/// <summary>
		///     The indentation string of the next line.
		/// </summary>
		string NextLineIndent { get; }

		/// <summary>
		///     The indent string on the beginning of the current line.
		/// </summary>
		string CurrentIndent { get; }

		/// <summary>
		///     True if the current line needs to be reindented.
		/// </summary>
		bool NeedsReindent { get; }

		/// <summary>
		///     The current offset of the engine.
		/// </summary>
		int Offset { get; }

		int LineNumber { get; }
		int LineOffset { get; }

		/// <summary>
		///     If this is true, the engine should try to adjust its indent 
		///     levels to manual user's corrections, even if they are wrong.
		/// </summary>
		bool EnableCustomIndentLevels { get; set; }

		/// <summary>
		///     Pushes a new char into the engine which calculates the new
		///     indentation levels.
		/// </summary>
		/// <param name="ch">
		///     A new character.
		/// </param>
		void Push(char ch);

		/// <summary>
		///     Resets the engine.
		/// </summary>
		void Reset();

		/// <summary>
		///     Clones the engine and preserves the current state.
		/// </summary>
		/// <returns>
		///     An indentical clone which can operate without interference
		///     with this engine.
		/// </returns>
		new IDocumentIndentEngine Clone();
	}
}