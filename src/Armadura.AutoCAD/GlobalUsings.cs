// Namespaces do AutoCAD 2026. O projeto GstarCAD tem o equivalente com GrxCAD.*.
// GraphicsInterface e Runtime entram com apelido: ambos têm nomes que colidem
// (Gi.Polyline × DatabaseServices.Polyline; Rt.Exception × System.Exception).
global using Autodesk.AutoCAD.ApplicationServices;
global using Autodesk.AutoCAD.Colors;
global using Autodesk.AutoCAD.DatabaseServices;
global using Autodesk.AutoCAD.EditorInput;
global using Autodesk.AutoCAD.Geometry;
global using Gi = Autodesk.AutoCAD.GraphicsInterface;
global using Rt = Autodesk.AutoCAD.Runtime;
global using CadApp = Autodesk.AutoCAD.ApplicationServices.Application;
// Paleta: mesma API nos dois CADs, namespace diferente.
global using PaletteSet = Autodesk.AutoCAD.Windows.PaletteSet;
global using PaletteSetStyles = Autodesk.AutoCAD.Windows.PaletteSetStyles;
// Foco de volta ao desenho (equivale a GrxCAD.Internal.Utils no GstarCAD).
global using CadUtils = Autodesk.AutoCAD.Internal.Utils;
// O AutoCAD tem um EnumConverter em DatabaseServices que colide com o do PropertyGrid.
global using EnumConverter = System.ComponentModel.EnumConverter;
global using StandardValuesCollection = System.ComponentModel.TypeConverter.StandardValuesCollection;
