namespace Migrations
open SimpleMigrations

[<Migration(2L, "Create Log Table")>]
type CreateLogTable() =
  inherit Migration()
  override __.Up() =
    base.Execute("""
    CREATE TABLE logs
    (
      raise_date timestamp without time zone,
      level character varying(50) COLLATE pg_catalog."default",
      elapsed integer,
      status integer,
      method text COLLATE pg_catalog."default",
      function text COLLATE pg_catalog."default",
      parameters text COLLATE pg_catalog."default",
      query text COLLATE pg_catalog."default",
      detail text COLLATE pg_catalog."default",
      exception text COLLATE pg_catalog."default"
    );
    """)

  override __.Down() =
    base.Execute("""
    DROP TABLE IF EXISTS logs;
""")
