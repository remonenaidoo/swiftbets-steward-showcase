-- Public status page incidents (D182); staff write them from the console, anyone reads them.
CREATE SCHEMA IF NOT EXISTS status;

CREATE TABLE status.incidents
(
    incident_id uuid        PRIMARY KEY,
    title       text        NOT NULL,
    impact      text        NOT NULL,
    status      text        NOT NULL,
    components  text[]      NOT NULL,
    created_at  timestamptz NOT NULL,
    updated_at  timestamptz NOT NULL,
    resolved_at timestamptz NULL,
    created_by  text        NOT NULL
);
CREATE INDEX ix_status_incidents_created ON status.incidents (created_at DESC);

CREATE TABLE status.incident_updates
(
    update_id   bigserial   PRIMARY KEY,
    incident_id uuid        NOT NULL REFERENCES status.incidents (incident_id),
    at          timestamptz NOT NULL,
    status      text        NOT NULL,
    message     text        NOT NULL,
    created_by  text        NOT NULL
);
CREATE INDEX ix_status_incident_updates ON status.incident_updates (incident_id, at DESC);
