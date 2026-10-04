-- Removes the status page tables; incidents published on the page are lost.
DROP TABLE IF EXISTS status.incident_updates;
DROP TABLE IF EXISTS status.incidents;
DROP SCHEMA IF EXISTS status;
