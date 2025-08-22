using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSCS.ViewModels
{
    public class ShellMenuItem
    {
        public string Title { get; }
        public ShellRoute Route { get; }
        public string IconGlyph { get; } // MDL2 glyph like "\uE10F" (optional)

        public ShellMenuItem(string title, ShellRoute route, string iconGlyph = "")
        {
            Title = title;
            Route = route;
            IconGlyph = iconGlyph;
        }
    }
}