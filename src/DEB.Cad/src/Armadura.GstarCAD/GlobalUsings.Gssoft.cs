// Namespaces do GstarCAD 2024 em diante (Gssoft.Gscad.*; até a 2023 eram GrxCAD.*, em GlobalUsings.cs).
// GraphicsInterface e Runtime entram com apelido: ambos têm nomes que colidem
// (Gi.Polyline × DatabaseServices.Polyline; Rt.Exception × System.Exception).
global using Gssoft.Gscad.ApplicationServices;
global using Gssoft.Gscad.Colors;
global using Gssoft.Gscad.DatabaseServices;
global using Gssoft.Gscad.EditorInput;
global using Gssoft.Gscad.Geometry;
global using Gi = Gssoft.Gscad.GraphicsInterface;
global using Rt = Gssoft.Gscad.Runtime;
global using CadApp = Gssoft.Gscad.ApplicationServices.Application;
global using PaletteSet = Gssoft.Gscad.Windows.PaletteSet;
global using PaletteSetStyles = Gssoft.Gscad.Windows.PaletteSetStyles;
// Foco de volta ao desenho.
global using CadUtils = Gssoft.Gscad.Internal.Utils;
// Como no AutoCAD, há um EnumConverter em DatabaseServices que colide com o do PropertyGrid.
global using EnumConverter = System.ComponentModel.EnumConverter;
global using StandardValuesCollection = System.ComponentModel.TypeConverter.StandardValuesCollection;
