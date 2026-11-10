-- SQL_Latin1_General_CP1_CI_AS source columns compare case-insensitively and accent-sensitively.
-- ICU preserves that policy; Unicode sort order is not asserted identical across engines.
CREATE COLLATION IF NOT EXISTS public.hawala_sqlserver_ci
    (provider=icu,locale='und-u-ks-level2',deterministic=false);
DO $body$
DECLARE item record;
BEGIN
    IF EXISTS(SELECT 1 FROM hawala.schema_versions WHERE version=2) THEN RETURN; END IF;
    FOR item IN SELECT table_name,column_name FROM information_schema.columns
        WHERE table_schema='public' AND data_type='text'
    LOOP
        EXECUTE format('ALTER TABLE public.%I ALTER COLUMN %I TYPE text COLLATE public.hawala_sqlserver_ci',item.table_name,item.column_name);
    END LOOP;
    INSERT INTO hawala.schema_versions(version) VALUES(2);
END $body$;
