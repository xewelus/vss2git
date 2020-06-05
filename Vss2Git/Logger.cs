/* Copyright 2009 HPDI, LLC
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Hpdi.Vss2Git
{
    /// <summary>
    /// Writes log messages to an optional stream.
    /// </summary>
    /// <author>Trevor Robinson</author>
    class Logger : IDisposable
    {
        public static readonly Logger Null = new Logger((Stream)null);

        private const string sectionSeparator = "------------------------------------------------------------";

        private readonly Logger commonLogger;
        private readonly Stream baseStream;
		private readonly Encoding encoding;
        private readonly IFormatProvider formatProvider;

        public Logger(string filename, Logger commonLogger)
            : this(new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
	        this.commonLogger = commonLogger;
        }

        public Logger(Stream baseStream)
            : this(baseStream, Encoding.Default, CultureInfo.InvariantCulture)
        {
        }

        public Logger(Stream baseStream, Encoding encoding, IFormatProvider formatProvider)
        {
            this.baseStream = baseStream;
            this.encoding = encoding;
            this.formatProvider = formatProvider;
        }

        public void Dispose()
        {
            if (baseStream != null)
            {
                baseStream.Dispose();
            }
		}

        public void Write(bool value)
        {
            if (baseStream != null)
            {
                Write(value.ToString());
            }
			this.commonLogger?.Write(value);
        }

        public void Write(char value)
        {
            if (baseStream != null)
            {
                Write(value.ToString());
            }
			this.commonLogger?.Write(value);
        }

		public void Write(char[] buffer)
        {
            if (baseStream != null && buffer != null)
            {
                Write(buffer, 0, buffer.Length);
            }
			this.commonLogger?.Write(buffer);
        }

		public void Write(decimal value)
        {
            if (baseStream != null)
            {
                Write(value.ToString());
            }
			this.commonLogger?.Write(value);
        }

		public void Write(double value)
        {
            if (baseStream != null)
            {
                Write(value.ToString());
            }
			this.commonLogger?.Write(value);
        }

		public void Write(float value)
        {
            if (baseStream != null)
            {
                Write(value.ToString());
            }
			this.commonLogger?.Write(value);
        }

		public void Write(int value)
        {
            if (baseStream != null)
            {
                Write(value.ToString());
            }
			this.commonLogger?.Write(value);
        }

		public void Write(long value)
        {
            if (baseStream != null)
            {
                Write(value.ToString());
            }
			this.commonLogger?.Write(value);
        }

		public void Write(object value)
        {
            if (baseStream != null && value != null)
            {
                Write(value.ToString());
            }
			this.commonLogger?.Write(value);
        }

		public void Write(string value)
        {
            if (baseStream != null && value != null)
            {
                WriteInternal(value);
                baseStream.Flush();
            }
			this.commonLogger?.Write(value);
        }

		public void Write(uint value)
        {
            if (baseStream != null)
            {
                Write(value.ToString());
            }
			this.commonLogger?.Write(value);
        }

		public void Write(ulong value)
        {
            if (baseStream != null)
            {
                Write(value.ToString());
            }
			this.commonLogger?.Write(value);
        }

		public void Write(string format, params object[] arg)
        {
            if (baseStream != null && arg != null)
            {
                Write(string.Format(formatProvider, format, arg));
            }
			this.commonLogger?.Write(format, arg);
        }

		public void Write(char[] buffer, int index, int count)
        {
            if (baseStream != null && buffer != null)
            {
                WriteInternal(buffer, index, count);
                baseStream.Flush();
            }
			this.commonLogger?.Write(buffer, index, count);
        }

		public void WriteLine()
        {
            Write(Environment.NewLine);
			this.commonLogger?.WriteLine();
        }

        public void WriteLine(object value)
        {
            if (baseStream != null)
            {
                WriteInternal(value.ToString());
                WriteLine();
            }
			this.commonLogger?.WriteLine(value);
        }

		public void WriteLine(string value)
        {
            if (baseStream != null)
            {
                WriteInternal(value);
                WriteLine();
            }
			this.commonLogger?.WriteLine(value);
        }

		public void WriteLine(string format, params object[] arg)
        {
            if (baseStream != null && arg != null)
            {
                WriteInternal(string.Format(formatProvider, format, arg));
                WriteLine();
            }
			this.commonLogger?.WriteLine(format, arg);
        }

		public void WriteSectionSeparator()
        {
            WriteLine(sectionSeparator);
			this.commonLogger?.WriteSectionSeparator();
        }

		private void WriteInternal(string value)
        {
            var bytes = encoding.GetBytes(value);
            baseStream.Write(bytes, 0, bytes.Length);
        }

        private void WriteInternal(char[] buffer, int index, int count)
        {
            var bytes = encoding.GetBytes(buffer, index, count);
            baseStream.Write(bytes, 0, bytes.Length);
        }
    }
}
