// Namespaces do ZWCAD (ZwSoft.ZwCAD.*). Os projetos GstarCAD e AutoCAD têm o equivalente.
// GraphicsInterface e Runtime entram com apelido: ambos têm nomes que colidem
// (Gi.Polyline × DatabaseServices.Polyline; Rt.Exception × System.Exception).
global using ZwSoft.ZwCAD.ApplicationServices;
global using ZwSoft.ZwCAD.Colors;
global using ZwSoft.ZwCAD.DatabaseServices;
global using ZwSoft.ZwCAD.EditorInput;
global using ZwSoft.ZwCAD.Geometry;
global using Gi = ZwSoft.ZwCAD.GraphicsInterface;
global using Rt = ZwSoft.ZwCAD.Runtime;
global using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;
global using PaletteSet = ZwSoft.ZwCAD.Windows.PaletteSet;
global using PaletteSetStyles = ZwSoft.ZwCAD.Windows.PaletteSetStyles;
// Foco de volta ao desenho.
global using CadUtils = ZwSoft.ZwCAD.Internal.Utils;
// Como no AutoCAD, há um EnumConverter em DatabaseServices que colide com o do PropertyGrid.
global using EnumConverter = System.ComponentModel.EnumConverter;
global using StandardValuesCollection = System.ComponentModel.TypeConverter.StandardValuesCollection;
