"""Two owned Unity fixtures: native bridge identity, reload and shutdown checks."""
import argparse
import json
import pathlib
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request


def wait_for(predicate, seconds=100):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        value = predicate()
        if value:
            return value
        time.sleep(0.25)
    raise TimeoutError("Unity fixture did not reach the expected state")


def descriptor(project, pid):
    path = project / "Library" / f"OptifunityBridge-{pid}.json"
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        return None


def request(connection, identity=None, path="/status"):
    identity = identity or connection
    headers = {"X-Optifunity-Token": identity["token"],
               "X-Optifunity-Project": urllib.parse.quote(identity["projectPath"], safe=""),
               "X-Optifunity-Instance": identity["instanceId"]}
    req = urllib.request.Request(connection["url"] + path, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=6) as response:
            return response.status, json.loads(response.read())
    except urllib.error.HTTPError as error:
        return error.code, None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("unity")
    parser.add_argument("project_a", type=pathlib.Path)
    parser.add_argument("project_b", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    projects = [args.project_a.resolve(), args.project_b.resolve()]
    if projects[0] == projects[1]:
        raise ValueError("Native Unity requires distinct fixture directories")
    for project in projects:
        if not (project / ".optifunity-fixture").is_file():
            raise ValueError("Refusing to operate on an unowned project")
        if (project / "Temp/UnityLockfile").exists():
            raise ValueError("Close the fixture Editor before live validation")
    processes, checks = [], []
    try:
        for project in projects:
            for marker in ("bridge-ready.txt", "bridge-stop.txt", "bridge-reload.txt"):
                (project / marker).unlink(missing_ok=True)
            processes.append(subprocess.Popen([
                args.unity, "-batchmode", "-nographics", "-projectPath", str(project),
                "-executeMethod", "Optifunity.Tests.LiveBridgeHarness.Hold",
                "-logFile", str(project / "live-bridge.log")
            ], creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)))
        for project, process in zip(projects, processes):
            wait_for(lambda: (project / "bridge-ready.txt").exists() and descriptor(project, process.pid))
        connections = [descriptor(p, process.pid) for p, process in zip(projects, processes)]
        assert connections[0]["instanceId"] != connections[1]["instanceId"]
        assert connections[0]["url"] != connections[1]["url"]
        checks.append("Two simultaneous Editors have distinct endpoints and identities")
        for index, connection in enumerate(connections):
            status, data = request(connection)
            assert status == 200 and data["instanceId"] == connection["instanceId"]
            assert pathlib.Path(data["projectPath"]).resolve() == projects[index]
            assert request(connection, connections[1-index])[0] == 403
            spoof = dict(connection, instanceId=connections[1-index]["instanceId"])
            assert request(connection, spoof)[0] == 403
            assert request(connection, dict(connection, token="invalid"))[0] == 403
            assert request(connection, path="/optimize")[0] == 403
        checks.append("Both Editors reject cross-project, same-path wrong-instance, wrong-token and mutation requests")
        old = connections[0]
        (projects[0] / "bridge-reload.txt").write_text("reload")
        def reloaded():
            fresh = descriptor(projects[0], processes[0].pid)
            return fresh if fresh and fresh["instanceId"] != old["instanceId"] and (projects[0] / "bridge-ready.txt").exists() else None
        fresh = wait_for(reloaded)
        def responsive():
            try:
                status, data = request(fresh)
                return status == 200 and data["instanceId"] == fresh["instanceId"]
            except (TimeoutError, urllib.error.URLError):
                return False
        wait_for(responsive, seconds=40)
        status, context = request(fresh, path="/assistant-context")
        assert status == 200 and context["assistant"]["instanceId"] == fresh["instanceId"]
        assert context["assistant"]["project"] == fresh["projectPath"]
        assert any(rule["ruleId"] == "code.boxing" for rule in context["assistant"]["knowledge"])
        assert request(fresh, old)[0] == 403
        assert request(connections[1])[0] == 200
        checks.append("Native script reload waits for Editor readiness, rotates identity, serves sourced assistant context, rejects old credentials and preserves the other Editor")
    finally:
        for project, process in zip(projects, processes):
            if process.poll() is None:
                (project / "bridge-stop.txt").write_text("stop")
        for process in processes:
            process.wait(timeout=45)
    for project, process in zip(projects, processes):
        assert process.returncode == 0
        assert not (project / "Library" / f"OptifunityBridge-{process.pid}.json").exists()
    checks.append("Both normal shutdowns remove discovery descriptors")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"result": "Passed", "checks": checks,
        "same_path_note": "Wrong-instance request simulated on one path; Unity prevents opening the same project twice.",
        "tested_at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}, indent=2))
    print(json.dumps({"result": "Passed", "check_groups": len(checks)}))


if __name__ == "__main__":
    main()
