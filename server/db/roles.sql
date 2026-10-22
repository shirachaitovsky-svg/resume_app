-- Minimal database roles for production (story 4.1). Run with psql as a superuser (or the database owner with CREATEROLE):
--
--   psql "<admin connection string>" -v ON_ERROR_STOP=1 -v migrator_password='...' -v app_password='...' -f server/db/roles.sql
--
-- Always pass -v ON_ERROR_STOP=1 as well when automating (the script also sets it): psql then exits non-zero on any error, including a missing variable.
-- Optional variables: -v migrator_name=migrator -v app_name=app (these are the defaults).
--
-- migrator: owns the schema and everything the migrations create. Used only through DATABASE_URL_MIGRATOR, when the server migrates.
-- app:      the running application (DATABASE_URL). Cannot change the schema; cannot UPDATE, DELETE or TRUNCATE audit_log.
--
-- The script is safe to run again. Run it once before the first start, and AGAIN after the first migration (and after any migration
-- that adds a table): audit_log only exists after the migration, and the restriction on it is applied here, not by the migration.
-- To adopt a database that was created by a single user, first run:  REASSIGN OWNED BY <old user> TO <migrator>;

\set ON_ERROR_STOP on

\if :{?migrator_name}
\else
  \set migrator_name migrator
\endif
\if :{?app_name}
\else
  \set app_name app
\endif
\if :{?migrator_password}
\else
  DO $$ BEGIN RAISE EXCEPTION 'Missing -v migrator_password=...'; END $$;
\endif
\if :{?app_password}
\else
  DO $$ BEGIN RAISE EXCEPTION 'Missing -v app_password=...'; END $$;
\endif

-- Roles (created once; an existing role only gets its password and login right refreshed).
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'migrator_name', :'migrator_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'migrator_name') \gexec
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'app_name', :'app_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'app_name') \gexec
SELECT format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', :'migrator_name', :'migrator_password') \gexec
SELECT format('ALTER ROLE %I WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', :'app_name', :'app_password') \gexec

-- The schema: owned by migrator (so it can create tables and own __EFMigrationsHistory); nobody else can create objects in it.
ALTER SCHEMA public OWNER TO :"migrator_name";
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM :"app_name";
GRANT USAGE ON SCHEMA public TO :"app_name";

-- Tables and sequences the migrator creates from now on: the app can read, insert and update, never delete or change the schema.
ALTER DEFAULT PRIVILEGES FOR ROLE :"migrator_name" IN SCHEMA public GRANT SELECT, INSERT, UPDATE ON TABLES TO :"app_name";
ALTER DEFAULT PRIVILEGES FOR ROLE :"migrator_name" IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO :"app_name";

-- The same for what already exists.
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA public TO :"app_name";
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO :"app_name";

-- DELETE only on the tables the code deletes from: expired rate-limit counters, and Identity users and their roles
-- (the first-admin seeder rolls back a half-created user).
SELECT format('GRANT DELETE ON public.%I TO %I', t, :'app_name')
FROM unnest(ARRAY['rate_limit_counters', 'users', 'user_roles']) AS t
WHERE to_regclass('public.' || t) IS NOT NULL \gexec

-- The audit log is append-only: the app can read and add rows, nothing else.
SELECT format('REVOKE ALL ON public.audit_log FROM %I', :'app_name') WHERE to_regclass('public.audit_log') IS NOT NULL \gexec
SELECT format('GRANT SELECT, INSERT ON public.audit_log TO %I', :'app_name') WHERE to_regclass('public.audit_log') IS NOT NULL \gexec

-- The migration history is the migrator's alone.
SELECT format('REVOKE ALL ON public."__EFMigrationsHistory" FROM %I', :'app_name') WHERE to_regclass('public."__EFMigrationsHistory"') IS NOT NULL \gexec
