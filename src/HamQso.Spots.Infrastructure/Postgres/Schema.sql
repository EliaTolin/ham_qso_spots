-- Archive of the spots server (F24). Idempotent: run at every startup.
-- 7-day window: rows older than that are deleted by the Host every hour.
create table if not exists dx_spots (
    program          text             not null,
    spot_key         text             not null,
    callsign         text             not null,
    frequency_khz    double precision not null,
    spot_time        timestamptz      not null,
    feeds            text[]           not null,
    spotters         text[]           not null default '{}',
    band             text,
    mode             text             not null default '',
    mode_source      text,
    mode_category    text,
    reference        text,
    location         text,
    dxcc             integer,
    continent        text,
    latitude         double precision,
    longitude        double precision,
    comment          text,
    spotter_locator  text,
    received_at      timestamptz      not null default now(),
    primary key (program, spot_key)
);

create index if not exists dx_spots_spot_time on dx_spots (spot_time);
create index if not exists dx_spots_callsign on dx_spots (callsign, spot_time desc);

-- seq (27/10/2026): order of the saves, for the apps' since_seq. Added in place
-- so that the table already in production upgrades itself; existing rows get one.
create sequence if not exists dx_spots_seq_counter;
alter table dx_spots add column if not exists seq bigint;
update dx_spots set seq = nextval('dx_spots_seq_counter') where seq is null;
alter table dx_spots alter column seq set default nextval('dx_spots_seq_counter');
alter table dx_spots alter column seq set not null;
create index if not exists dx_spots_seq_idx on dx_spots (seq);
create index if not exists dx_spots_program_time_idx on dx_spots (program, spot_time desc);

-- Activation key (27/10/2026): xOTA spots are one row per activation and band
-- ("K8RLE_US-6701_40m"). Rows saved before with the program's own id as key
-- (digits only, no "_") are removed; the feeds send the current ones again.
delete from dx_spots where program <> 'dx' and position('_' in spot_key) = 0;

-- Details for the apps (27/10/2026): reference name, SOTA summit data, how many
-- times an activation was spotted (counted here, +1 at every newer spot), LoTW/eQSL.
alter table dx_spots add column if not exists reference_name text;
alter table dx_spots add column if not exists activator_name text;
alter table dx_spots add column if not exists altitude_m integer;
alter table dx_spots add column if not exists points integer;
alter table dx_spots add column if not exists spot_count integer not null default 1;
alter table dx_spots add column if not exists lotw boolean;
alter table dx_spots add column if not exists eqsl boolean;
