// Carga inicial del catálogo a partir de la lista de precios del local (T-19).
//
//   dotnet run --project herramientas/Condor.CargaCatalogo -- db/datos/lista_precios.csv --simular
//   dotnet run --project herramientas/Condor.CargaCatalogo -- db/datos/lista_precios.csv --config "C:\CondorVentas"
//
// --simular  solo revisa el archivo y muestra los errores; no toca la base de datos.
// --config   carpeta donde está condor.json (por defecto, la carpeta actual).
// Los productos que ya existen se omiten, así que se puede ejecutar dos veces.

using Condor.Datos;
using Condor.Datos.Importacion;
using Condor.Dominio.Validacion;

if (args.Length == 0 || args.Contains("--ayuda"))
{
    Console.WriteLine("Uso: CargaCatalogo <lista_precios.csv> [--simular] [--config <carpeta de condor.json>]");
    return 1;
}

var ruta = args[0];
var simular = args.Contains("--simular");
var indiceConfig = Array.IndexOf(args, "--config");
var carpetaConfig = indiceConfig >= 0 && indiceConfig + 1 < args.Length ? args[indiceConfig + 1] : Directory.GetCurrentDirectory();

if (!File.Exists(ruta))
{
    Console.Error.WriteLine($"No existe el archivo {ruta}.");
    return 1;
}

var filas = LectorListaPrecios.LeerArchivo(ruta);
var validas = filas.Where(f => f.EsValida).ToList();
foreach (var fila in filas.Where(f => !f.EsValida))
    Console.WriteLine($"  Línea {fila.Linea}: {fila.Error}");
Console.WriteLine($"{validas.Count} productos válidos y {filas.Count - validas.Count} filas con error en {Path.GetFileName(ruta)}.");

if (simular)
{
    Console.WriteLine("Simulación: no se guardó nada.");
    return filas.Count == validas.Count ? 0 : 2;
}

ConfiguracionConexion config;
try
{
    config = ConfiguracionConexion.Cargar(carpetaConfig);
}
catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

await using var conexiones = new FabricaConexiones(config, "CargaCatalogo");
await new Migrador(conexiones).AplicarAsync();
var repo = new ProductoRepositorio(conexiones);

int nuevos = 0, existentes = 0;
foreach (var fila in validas)
{
    try
    {
        await repo.RegistrarAsync(fila.Producto!);
        nuevos++;
    }
    catch (ProductoDuplicadoException)
    {
        existentes++;
    }
}

Console.WriteLine($"Listo: {nuevos} productos nuevos, {existentes} ya existían (omitidos), en {config}.");
return filas.Count == validas.Count ? 0 : 2;
