SELECT incident_id AS IncidentId, status AS Status, message AS Message, at AS At
FROM status.incident_updates
WHERE incident_id = ANY(@Ids)
ORDER BY at DESC, update_id DESC;
