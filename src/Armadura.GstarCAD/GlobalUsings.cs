// Namespaces do GstarCAD. No projeto AutoCAD, este arquivo troca GrxCAD por Autodesk.AutoCAD.
// GraphicsInterface e Runtime entram com apelido: ambos têm nomes que colidem
// (Gi.Polyline × DatabaseServices.Polyline; Rt.Exception × System.Exception).
global using GrxCAD.ApplicationServices;
global using GrxCAD.Colors;
global using GrxCAD.DatabaseServices;
global using GrxCAD.EditorInput;
global using GrxCAD.Geometry;
global using Gi = GrxCAD.GraphicsInterface;
global using Rt = GrxCAD.Runtime;
global using CadApp = GrxCAD.ApplicationServices.Application;
