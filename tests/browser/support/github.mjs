// GitHub for the browser suites. Two backends with one interface:
//
// * live (CAUSA_GITHUB_TOKEN set): the real GitHub repository and branch; nothing is intercepted. This is the
//   only backend whose evidence counts for INV-037 (isolation: reload from the real repository).
// * fake (default): a stateful Contents API double installed with context.route. It keeps files across browser
//   contexts of one test (the store object is shared), checks the blob sha on every PUT (409 on a stale sha, 422
//   when a file exists and no sha is sent), and can inject one fault (status and body) for the negative scenarios.
//
// The application never knows which backend answers: it sees GitHub's documented shapes either way.
import { createHash } from "node:crypto";

export const destination = {
  owner: process.env.CAUSA_GITHUB_OWNER ?? "kemiller2002",
  repository: process.env.CAUSA_GITHUB_REPOSITORY ?? "indy-init-rehearsal-009",
  branch: process.env.CAUSA_GITHUB_BRANCH ?? "causa-data",
};

export const live = Boolean(process.env.CAUSA_GITHUB_TOKEN);
export const token = process.env.CAUSA_GITHUB_TOKEN ?? "fake-token-for-the-contents-double";

const api = "https://api.github.com";
const base64 = (text) => Buffer.from(text, "utf8").toString("base64");
const blobSha = (text) => createHash("sha1").update(`blob ${Buffer.byteLength(text)}\0${text}`).digest("hex");

export const createStore = () => ({ files: new Map(), faults: [], writes: [] });

/** Queue a fault: the next request matching method (and path fragment) is answered with status and body. */
export const injectFault = (store, fault) => store.faults.push(fault);

const json = (route, status, body) =>
  route.fulfill({ status, contentType: "application/json; charset=utf-8", headers: { "access-control-allow-origin": "*" }, body: typeof body === "string" ? body : JSON.stringify(body) });

/** Install the fake on a browser context (no-op against live GitHub). */
export const install = async (context, store) => {
  if (live) return;
  const prefix = `/repos/${destination.owner}/${destination.repository}`;
  await context.route(`${api}/**`, async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    if (method === "OPTIONS") return route.fulfill({ status: 204, headers: { "access-control-allow-origin": "*", "access-control-allow-headers": "*", "access-control-allow-methods": "GET, PUT" } });

    const fault = store.faults.findIndex((f) => f.method === method && (!f.path || url.pathname.includes(f.path)));
    if (fault >= 0 && store.faults[fault].applyThenAbort) {
      // GitHub applies the write, but its answer never reaches the browser.
      store.faults.splice(fault, 1);
      const body = JSON.parse(request.postData() ?? "{}");
      const path = decodeURIComponent(url.pathname.slice(`${prefix}/contents/`.length));
      const text = Buffer.from(body.content, "base64").toString("utf8");
      store.files.set(path, { text, sha: blobSha(text) });
      store.writes.push({ path, message: body.message, branch: body.branch });
      return route.abort("connectionreset");
    }
    if (fault >= 0) {
      const [f] = store.faults.splice(fault, 1);
      if (f.abort) return route.abort(f.abort);
      return json(route, f.status, f.body ?? { message: "injected fault" });
    }
    if ((request.headers()["authorization"] ?? "") !== `Bearer ${token}`) return json(route, 401, { message: "Bad credentials" });
    if (!url.pathname.startsWith(prefix)) return json(route, 404, { message: "Not Found" });

    const rest = url.pathname.slice(prefix.length);
    if (rest === "" && method === "GET") return json(route, 200, { full_name: `${destination.owner}/${destination.repository}`, permissions: { push: true } });
    if (!rest.startsWith("/contents/")) return json(route, 404, { message: "Not Found" });
    const path = decodeURIComponent(rest.slice("/contents/".length));

    if (method === "GET") {
      const file = store.files.get(path);
      if (file) return json(route, 200, { type: "file", path, sha: file.sha, encoding: "base64", content: base64(file.text) });
      const children = [...store.files.keys()].filter((p) => p.startsWith(`${path}/`));
      if (children.length > 0) return json(route, 200, children.map((p) => ({ type: "file", name: p.slice(path.length + 1), path: p })));
      return json(route, 404, { message: "Not Found" });
    }
    if (method === "PUT") {
      const body = JSON.parse(request.postData() ?? "{}");
      const existing = store.files.get(path);
      if (existing && !body.sha) return json(route, 422, { message: "Invalid request.\n\n\"sha\" wasn't supplied." });
      if (existing && body.sha !== existing.sha) return json(route, 409, { message: `${path} does not match ${body.sha}` });
      if (!existing && body.sha) return json(route, 409, { message: `${path} does not match ${body.sha}` });
      const text = Buffer.from(body.content, "base64").toString("utf8");
      const sha = blobSha(text);
      store.files.set(path, { text, sha });
      store.writes.push({ path, message: body.message, branch: body.branch });
      return json(route, existing ? 200 : 201, { content: { path, sha }, commit: { sha: blobSha(`${path}:${store.writes.length}`) } });
    }
    return json(route, 405, { message: "Method not allowed" });
  });
};

/** What GitHub holds for an investigation: the decoded causa.investigation/1 document, or null. */
export const storedDocument = async (store, request, id) => {
  const path = `causa/investigations/${id}.json`;
  if (!live) {
    const file = store.files.get(path);
    return file ? JSON.parse(file.text) : null;
  }
  const response = await request.get(`${api}/repos/${destination.owner}/${destination.repository}/contents/${path}?ref=${destination.branch}&probe=${Date.now()}`, {
    headers: { authorization: `Bearer ${token}`, accept: "application/vnd.github+json" },
  });
  if (response.status() !== 200) return null;
  const body = await response.json();
  return JSON.parse(Buffer.from(body.content, "base64").toString("utf8"));
};

/** Overwrite a saved document behind the application's back (conflict and malformed-reload scenarios; fake only). */
export const tamper = (store, id, text) => {
  const path = `causa/investigations/${id}.json`;
  store.files.set(path, { text, sha: blobSha(text) });
};
