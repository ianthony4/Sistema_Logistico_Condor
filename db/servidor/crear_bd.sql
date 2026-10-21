-- Crea el usuario de la aplicación y las bases de datos (T-02).
-- Ejecutar una sola vez en la PC principal, con el superusuario postgres:
--   psql -U postgres -f db/servidor/crear_bd.sql
-- Antes, cambiar la clave de la línea siguiente (no subir la clave real).

\set clave_app 'CAMBIAR_ESTA_CLAVE'

SELECT 'CREATE ROLE condor_app LOGIN PASSWORD ' || quote_literal(:'clave_app')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'condor_app')
\gexec

SELECT 'CREATE DATABASE condor OWNER condor_app ENCODING ''UTF8'' TEMPLATE template0'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'condor')
\gexec

SELECT 'CREATE DATABASE condor_pruebas OWNER condor_app ENCODING ''UTF8'' TEMPLATE template0'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'condor_pruebas')
\gexec

-- pg_trgm acelera la búsqueda de productos por nombre (T-15). Crear la
-- extensión requiere superusuario, por eso se hace aquí y no en la migración.
\connect condor
CREATE EXTENSION IF NOT EXISTS pg_trgm;
\connect condor_pruebas
CREATE EXTENSION IF NOT EXISTS pg_trgm;
