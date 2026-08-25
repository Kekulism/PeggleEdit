// This file is part of PeggleEdit.
// Copyright Ted John 2010 - 2011. http://tedtycoon.co.uk
//
// PeggleEdit is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// PeggleEdit is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with PeggleEdit. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IntelOrca.PeggleEdit.Tools.Levels
{
    /// <summary>
    /// A minimal, dependency-free JSON writer.
    /// </summary>
    /// <remarks>
    /// The project targets net472, where System.Text.Json is not in-box. Rather than
    /// add a NuGet dependency for a few hundred lines of output, this writes JSON
    /// directly. All numbers are written with InvariantCulture so that machines with
    /// comma decimal separators do not emit malformed JSON.
    /// </remarks>
    public class JsonTextWriter
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly Stack<bool> _isArray = new Stack<bool>();
        private bool _needsComma;
        private int _indent;

        public bool Indent { get; set; } = true;

        public override string ToString() => _sb.ToString();

        private void Punctuate()
        {
            if (_needsComma)
                _sb.Append(',');

            if (Indent && _sb.Length > 0)
            {
                _sb.Append('\n');
                _sb.Append(' ', _indent * 2);
            }

            _needsComma = true;
        }

        private void WriteName(string name)
        {
            if (name == null)
                return;

            WriteStringLiteral(name);
            _sb.Append(Indent ? ": " : ":");
        }

        public void StartObject(string name = null)
        {
            Punctuate();
            WriteName(name);
            _sb.Append('{');
            _isArray.Push(false);
            _indent++;
            _needsComma = false;
        }

        public void EndObject() => End('}');

        public void StartArray(string name = null)
        {
            Punctuate();
            WriteName(name);
            _sb.Append('[');
            _isArray.Push(true);
            _indent++;
            _needsComma = false;
        }

        public void EndArray() => End(']');

        private void End(char c)
        {
            _indent--;

            // An empty container stays on one line: {} rather than a blank body.
            if (_needsComma && Indent)
            {
                _sb.Append('\n');
                _sb.Append(' ', _indent * 2);
            }

            _sb.Append(c);
            _isArray.Pop();
            _needsComma = true;
        }

        /// <summary>
        /// Writes a compact inline array such as [12.5, 340]. Used for coordinate
        /// pairs, which would otherwise dominate the file with line breaks.
        /// </summary>
        public void WriteInlineNumbers(string name, params float[] values)
        {
            Punctuate();
            WriteName(name);
            _sb.Append('[');
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    _sb.Append(", ");
                _sb.Append(Num(values[i]));
            }
            _sb.Append(']');
        }

        public void Write(string name, string value)
        {
            if (value == null)
                return;

            Punctuate();
            WriteName(name);
            WriteStringLiteral(value);
        }

        public void Write(string name, bool value)
        {
            Punctuate();
            WriteName(name);
            _sb.Append(value ? "true" : "false");
        }

        public void Write(string name, int value)
        {
            Punctuate();
            WriteName(name);
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void Write(string name, float value)
        {
            Punctuate();
            WriteName(name);
            _sb.Append(Num(value));
        }

        public void WriteValue(string value)
        {
            Punctuate();
            WriteStringLiteral(value);
        }

        /// <summary>
        /// Rounds to 4 decimal places and strips the trailing ".0" that would
        /// otherwise appear on every whole-number coordinate.
        /// </summary>
        private static string Num(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return "0";

            double rounded = Math.Round((double)value, 4);
            return rounded.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private void WriteStringLiteral(string value)
        {
            _sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c > 0x7E)
                            _sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
