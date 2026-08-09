## 1. Remove REST Endpoint Code

- [x] 1.1 Delete `src/OutOfTheBox.Presentation/Execution/{RunEndpoints,FileTransferEndpoints,RepositoryEndpoints,SseWriter,SseProcessOutputSink,StartRunRequest,FileTransferRequest,CloneRepositoryRequest}.cs` and `src/OutOfTheBox.Presentation/Authentication/BearerAuthenticationFilter.cs`
- [x] 1.2 Update `src/OutOfTheBox.Host/Program.cs`: remove the three `Map*Endpoints()` calls for the deleted REST endpoints, fix the MCP tool-assembly marker type (`typeof(RunEndpoints)` → `typeof(CommandExecutionMcpTools)`), fix stale doc comments referencing REST
- [x] 1.3 Fix dangling `<see cref>` doc-comment references to the deleted types across `Presentation` (`BearerCredential`, `McpAuthenticationMiddleware`, `RepositoryFileDownloadEndpoints`, the three MCP tool classes) - confirm a clean `dotnet build` (0 warnings) across the whole solution
- [x] 1.4 Confirm `RepositoryFileDownloadEndpoints` (dashboard file download, cookie-authenticated) and every MCP tool class are completely unaffected - both were already independent of the deleted REST code

## 2. Remove/Rewrite REST-Dependent BehaviorTests

- [x] 2.1 Delete `DotnetCommandExecution`/`GitCommandExecution`/`FileTransfer`/`RepositoryRestEndpoints`/`Cancellation`/`ConcurrencyAndLocking`/`ServiceAuthentication` (`.feature` + matching `Steps.cs`) - each existed solely to exercise a REST endpoint now removed
- [x] 2.2 Port the locking/cancellation coverage those deleted files carried into `McpCommandExecution.feature` as pure-MCP scenarios (parallel-repos, same-kind busy rejection, lock-released-on-natural-completion, cross-kind dotnet-vs-git rejection, cancel-frees-the-repository) - no coverage gap left behind
- [x] 2.3 Rewrite the REST-driven steps inside `RepositoryManagement.feature`/`RunHistoryPersistenceSteps.cs`/`HostResourceMonitoringSteps.cs` to start their runs via MCP tool calls instead of raw REST HTTP requests; delete `RepositoryManagement.feature`'s one REST-only scenario ("REST cancel endpoint does not affect repository-management runs" - no MCP analogue, since MCP's `cancel_run` deliberately does the opposite)
- [x] 2.4 Delete `tests/OutOfTheBox.BehaviorTests/Support/SseTestClient.cs` once its last consumer is gone
- [x] 2.5 Fix the resulting ambiguous-step-binding collisions (several new pure-MCP scenarios reused step wording the now-deleted REST steps also used)
- [x] 2.6 Diagnose and fix a real BehaviorTests-infrastructure bug found while writing the new scenarios: a shared `HttpClient` with a still-open, undrained response blocking a later request on the same client (same class of bug `ConcurrencyAndLockingSteps.cs` already documented once); also shortened two scenarios' `HangingFixture` timeouts (30s → 3s) since their intentionally-never-cancelled child process otherwise outlived `WebApplicationFactory` disposal and delayed the test host's own exit
- [x] 2.7 Full `BehaviorTests` suite green: 52/52

## 3. Documentation

- [x] 3.1 Delete `skills/dotnet-command-service/` entirely - MCP tools are self-describing, no client-side skill/doc dependency needed
- [x] 3.2 Rewrite `README.md`'s REST-framed sections ("What it does", "How it works", the dashboard's list/clone note, "Running a Claude Code instance in the sbx sandbox") for the MCP-server-plus-dashboard-only interface shape
- [x] 3.3 Rewrite `INSTALL.md`'s REST-specific sections (Network & transport, Firewall, "Consuming Server-Sent Events"/"Downloading files" → "Connecting an MCP client", the after-restart cancel-run note)
- [x] 3.4 Add a `CHANGELOG.md` entry: edited the `Unreleased` `Added` list in place (not append-only history, since nothing has shipped yet) to remove REST-specific claims and reflect the MCP-only interface; added a `Removed` section documenting the REST API/skill removal explicitly, rather than pretending it never existed

## 4. OpenSpec Consistency

- [x] 4.1 This change's own `proposal.md`/`design.md`/`specs/*` (this directory)
- [x] 4.2 `openspec/changes/sbx-dotnet-command-service/specs/{dotnet-command-execution,git-command-execution,file-transfer}/spec.md`: all requirements were REST-specific - REMOVED, each with a Migration note pointing at the corresponding `mcp-*` requirement
- [x] 4.3 `openspec/changes/sbx-dotnet-command-service/specs/repository-management/spec.md`: two requirements MODIFIED (REST-reachable-subset → MCP-reachable-subset; the REST-cancel-refuses-a-clone requirement → `cancel_run`-accepts-a-clone) - the rest of this capability (dashboard-only actions) is untouched
- [x] 4.4 `openspec/changes/sbx-dotnet-command-service/specs/service-authentication/spec.md`: no delta needed (its requirements were already worded generically, not REST-specifically) - only its Purpose line's stale example list corrected directly
- [x] 4.5 `openspec/changes/sbx-mcp-server/specs/{mcp-server,mcp-command-execution,mcp-file-transfer,mcp-repository-access}/spec.md`: directly edited to remove now-stale REST comparisons in Purpose lines and requirement text (e.g. "shared with the REST API" → just the requirement itself; the two REST-vs-MCP cross-interface scenarios in `mcp-command-execution` replaced with pure-MCP equivalents matching the rewritten `McpCommandExecution.feature`)
- [x] 4.6 `openspec/changes/sbx-mcp-server/proposal.md`/`design.md`: left as historical narrative, with one cross-reference note each at the top pointing to this change, rather than rewritten - see design.md's own "why not rewrite them" rationale
- [x] 4.7 `openspec validate sbx-remove-rest-api --strict` passes

## 5. Final Verification

- [x] 5.1 Full solution `dotnet build`: 0 warnings, 0 errors
- [x] 5.2 `UnitTests`: 193/193 pass
- [x] 5.3 `ArchitectureTests`: 5/5 pass
- [x] 5.4 `BehaviorTests`: 52/52 pass
- [x] 5.5 Commit and push to `feature/sbx-mcp-server`; this remains an unmerged branch, PR not yet opened
