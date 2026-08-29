import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useRef, useState, type FormEvent } from "react";
import {
  createModel,
  getModels,
  updateModelVersion,
  uploadModelVersion,
  type ModelAdmin,
  type ModelVersionAdmin,
} from "./api";
import { CopyIdButton } from "./CopyIdButton";
import { ErrorState } from "./ErrorState";
import { useApiKey } from "./session";

// Admin > Models - the model registry (ADR-124,
// docs/architecture/model-registry-design.md). Versions are immutable
// file sets: upload creates the next number, Activate/Retire flips
// which one "latest Active" resolution picks at publish time. The
// ModelId chip is what a capability assignment's ModelId setting
// references - hence the copy button front and center.
export function ModelsAdmin() {
  const apiKey = useApiKey();
  const queryClient = useQueryClient();

  const modelsQuery = useQuery({
    queryKey: ["models-admin"],
    queryFn: () => getModels(apiKey),
  });

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [error, setError] = useState<string | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["models-admin"] });

  const createMutation = useMutation({
    mutationFn: () => createModel(apiKey, name.trim(), description.trim() || null),
    onSuccess: () => {
      setName("");
      setDescription("");
      invalidate();
    },
    onError: (err) => setError(err.message),
  });

  function handleCreate(e: FormEvent) {
    e.preventDefault();
    if (!name.trim() || createMutation.isPending) return;
    setError(null);
    createMutation.mutate();
  }

  if (modelsQuery.isError) {
    return <ErrorState message={modelsQuery.error.message} onRetry={() => modelsQuery.refetch()} />;
  }
  if (!modelsQuery.data) return <p>Loading models...</p>;

  return (
    <>
      <form className="list-toolbar" onSubmit={handleCreate}>
        <input
          type="text"
          className="list-search"
          placeholder="New model name (e.g. Sink Cleanliness Classifier)"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        <input
          type="text"
          className="list-search"
          placeholder="Description (optional)"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
        />
        <button type="submit" className="form-dialog-save" disabled={!name.trim() || createMutation.isPending}>
          {createMutation.isPending ? "Creating..." : "+ Add model"}
        </button>
      </form>

      {error && <p className="error">{error}</p>}

      {modelsQuery.data.length === 0 ? (
        <p>No models yet - add one, then upload its first version.</p>
      ) : (
        modelsQuery.data.map((model) => (
          <ModelCard key={model.modelId} model={model} onChanged={invalidate} onError={setError} />
        ))
      )}
    </>
  );
}

function ModelCard({
  model,
  onChanged,
  onError,
}: {
  model: ModelAdmin;
  onChanged: () => void;
  onError: (message: string) => void;
}) {
  const apiKey = useApiKey();
  const fileInput = useRef<HTMLInputElement>(null);
  const [notes, setNotes] = useState("");
  const [uploading, setUploading] = useState(false);

  async function handleUpload() {
    const files = Array.from(fileInput.current?.files ?? []);

    if (files.length === 0 || uploading) return;

    setUploading(true);

    try {
      await uploadModelVersion(apiKey, model.modelId, files, notes);
      setNotes("");
      if (fileInput.current) fileInput.current.value = "";
      onChanged();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Upload failed.");
    } finally {
      setUploading(false);
    }
  }

  async function toggleVersion(version: ModelVersionAdmin) {
    try {
      await updateModelVersion(
        apiKey,
        model.modelId,
        version.version,
        version.status === "Active" ? "Retired" : "Active",
        version.notes,
      );
      onChanged();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Update failed.");
    }
  }

  return (
    <div className="config-section">
      <div className="config-section-header">
        <div className="metric-cell-label">{model.name}</div>
        <div className="entity-row-subtitle" style={{ fontFamily: "monospace" }}>
          {model.modelId} <CopyIdButton value={model.modelId} />
        </div>
      </div>

      {model.description && <p className="entity-row-subtitle">{model.description}</p>}

      {model.versions.length === 0 ? (
        <p className="entity-row-subtitle">No versions uploaded yet.</p>
      ) : (
        <div className="entity-list">
          {model.versions.map((version) => (
            <div className="entity-row entity-row-static" key={version.version}>
              <div className="entity-row-main">
                <div>
                  <div className="entity-row-title">
                    v{version.version}
                    {version.notes ? ` — ${version.notes}` : ""}
                  </div>
                  <div className="entity-row-subtitle">
                    {version.files
                      .map((f) => `${f.name} (${formatSize(f.sizeBytes)})`)
                      .join(" + ")}
                  </div>
                </div>
              </div>
              <div className="entity-row-actions">
                <span className={`status ${version.status === "Active" ? "status-online" : "status-offline"}`}>
                  {version.status}
                </span>
                <button type="button" className="logs-button" onClick={() => toggleVersion(version)}>
                  {version.status === "Active" ? "Retire" : "Activate"}
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      <div className="list-toolbar" style={{ marginTop: "0.75rem", marginBottom: 0 }}>
        {/* One version = one atomic file set: pick the .onnx AND any
            companion files (.onnx.data) in a single selection. */}
        <input ref={fileInput} type="file" multiple className="list-search" />
        <input
          type="text"
          className="list-search"
          placeholder="Version notes (optional)"
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
        />
        <button type="button" className="logs-button" onClick={handleUpload} disabled={uploading}>
          {uploading ? "Uploading..." : "Upload new version"}
        </button>
      </div>
    </div>
  );
}

function formatSize(bytes: number): string {
  if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  if (bytes >= 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${bytes} B`;
}
