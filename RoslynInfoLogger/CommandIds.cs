using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoslynInfoLogger
{
    internal static class CommandIds
    {
        public static readonly Guid CommandSet = new Guid("{0b975514-7844-4900-87fb-3ec29482b2af}");

        public const int MenuGroup = 0x1020;
        public const int LogInfoCommandId = 0x100;
    }
}
