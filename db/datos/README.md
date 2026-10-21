# Carga inicial del catálogo (T-19)

`lista_precios.csv` es una **muestra** de 60 productos con precios de
referencia, para probar la carga. Antes del piloto se reemplaza por la lista
de precios real de la ferretería (la que hoy está en papel), digitada en Excel
con estas columnas y guardada como *CSV (delimitado por comas)* en UTF-8:

| nombre | precio | unidad |
|---|---|---|
| Cemento Portland tipo I x 42.5 kg | 32.50 | BLS |

- El precio es el de venta **con IGV**; se acepta `32.50` o `32,50`.
- La unidad es opcional (por defecto `UND`).
- Las filas con error se informan con su número de línea y no se cargan.

## Cómo cargarla

Primero se revisa el archivo sin tocar la base de datos:

```powershell
dotnet run --project herramientas/Condor.CargaCatalogo -- db/datos/lista_precios.csv --simular
```

Luego se carga en la base central, indicando la carpeta donde está `condor.json`:

```powershell
dotnet run --project herramientas/Condor.CargaCatalogo -- db/datos/lista_precios.csv --config "C:\CondorVentas"
```

Los productos que ya existen se omiten, así que se puede volver a ejecutar
después de corregir las filas con error.
