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
