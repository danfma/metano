# Changelog

All notable changes to Metano are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 2.4.0

_2026-06-03_


### ✨ Features

* add JS-interop primitives ([JsTuple], [JsCallable], tuple deconstruction) ([9dab074](https://github.com/danfma/metano/commit/9dab07464c3c37756d67d60024d1fed8e1d64828))
* add JSX/TSX code generation from C# components ([6e1279c](https://github.com/danfma/metano/commit/6e1279ce6431a3827d0664c5f42524400252b0f5))
* **build:** incremental skip + ItemGroup args for MetanoTranspile ([#230](https://github.com/danfma/metano/issues/230)) ([b531c94](https://github.com/danfma/metano/commit/b531c94a78edcc7c8746df6f5e262c1ab702084d))
* **compiler:** copy <MetanoAsset> static assets to generated output ([#199](https://github.com/danfma/metano/issues/199)) ([23078f6](https://github.com/danfma/metano/commit/23078f6866bb6a4c14d8f5dbbafdf3db5abd4174))

### 🐛 Bug Fixes

* **build:** consolidate Metano.Build targets + add MetanoEnabled opt-out ([#230](https://github.com/danfma/metano/issues/230)) ([618eede](https://github.com/danfma/metano/commit/618eede3b84eaa2f78f3cddb2fbf97df44d2f36e))
* **compiler:** address PR review findings on asset copy ([#199](https://github.com/danfma/metano/issues/199)) ([a0805cb](https://github.com/danfma/metano/commit/a0805cb308c032ae4095b12487937ebf5f8d4c51))
* correct JSX codegen lambda arity, text escaping, prop-rewrite coverage, and cross-package component imports ([d04ef79](https://github.com/danfma/metano/commit/d04ef79072749d17afaed3a9d5b69a85a3cd6d16)), closes [#002](https://github.com/danfma/metano/issues/002)
* handle IrLinqChain in JSX prop-reference rewrite and regenerate samples ([eb1cccc](https://github.com/danfma/metano/commit/eb1cccc2ec75f8c9b2bc2c23ff66e51bfaca54d3)), closes [#002](https://github.com/danfma/metano/issues/002)
* stop branded records from requesting an unused HashCode helper import ([e8692d2](https://github.com/danfma/metano/commit/e8692d284620065f216e864637f666655256821a))

### ♻️ Refactor

* **annotations:** hygiene sweep across Metano.Annotations ([#229](https://github.com/danfma/metano/issues/229)) ([54c82a1](https://github.com/danfma/metano/commit/54c82a11a8b3273d0087a5d7df28c084d06435e6))
* **compiler:** consolidate fold helpers + Dart duplication ([#225](https://github.com/danfma/metano/issues/225)) ([ced1abb](https://github.com/danfma/metano/commit/ced1abb0822c0e0abe424b81de7bd12969a11f44))
* **compiler:** dispatch JSON descriptor build on JsonPropertyKind enum ([#231](https://github.com/danfma/metano/issues/231)) ([f2efda6](https://github.com/danfma/metano/commit/f2efda6cedf2a0bdcc4e885973b93823d7ecb338))
* **compiler:** extract [Inline] cascade arm into thin rewriter ([#221](https://github.com/danfma/metano/issues/221)) ([5cfe60b](https://github.com/danfma/metano/commit/5cfe60b309096085f14c8586e94701acb38cd428))
* **compiler:** extract Emit/Import template lowering into rewriter ([#221](https://github.com/danfma/metano/issues/221)) ([b996868](https://github.com/danfma/metano/commit/b996868b122a6465ae09b9324a00510329c6eeaf))
* **compiler:** extract extension-method cascade arm into rewriter ([#221](https://github.com/danfma/metano/issues/221)) ([ae84f53](https://github.com/danfma/metano/commit/ae84f53cf815b98d1b3b426664ed665398c344d5))
* **compiler:** extract invocation cascade into rewriter chain ([#221](https://github.com/danfma/metano/issues/221)) ([460b9fb](https://github.com/danfma/metano/commit/460b9fbdaefcb7d4df780867f1821e6f982e505f))
* **compiler:** extract named helpers per naming convention ([e8bb9c2](https://github.com/danfma/metano/commit/e8bb9c2d50618ae91a0fe6d2fbe788470fda690e))
* **compiler:** extract TypeTransformer Parallel.For body ([#222](https://github.com/danfma/metano/issues/222)) ([ad20534](https://github.com/danfma/metano/commit/ad205346fc962d3ae6542aa3f5ffd1ff5ccb037c))
* **compiler:** hoist ImportCollector skip-list into a HashSet ([#224](https://github.com/danfma/metano/issues/224)) ([195dedf](https://github.com/danfma/metano/commit/195dedfc2462361ac9831639cdeaa8af59247c78))
* **compiler:** preserve concrete export kind in per-group cache ([#220](https://github.com/danfma/metano/issues/220)) ([fd29f23](https://github.com/danfma/metano/commit/fd29f23c6c8723396bb9c52c4d7dbbe6fc113f6f))
* **compiler:** split IrExpressionExtractor into themed partials ([#221](https://github.com/danfma/metano/issues/221)) ([22d691b](https://github.com/danfma/metano/commit/22d691b50a4b4053018fa2640083e35a1a5196fd))
* **compiler:** split IrToTsClassBridge into thematic partial files ([#223](https://github.com/danfma/metano/issues/223)) ([9c52ee8](https://github.com/danfma/metano/commit/9c52ee854554bfd03d9b5009c1b985fe1e77fe1f))
* **compiler:** split IrToTsClassEmitter.Transform into named phases ([#223](https://github.com/danfma/metano/issues/223)) ([4ff44fd](https://github.com/danfma/metano/commit/4ff44fd5265cb06dc40692697f64e2c54b5c5fc6))
* **compiler:** split TypeTransformer.BuildTypeStatements + drains ([#222](https://github.com/danfma/metano/issues/222)) ([5ef3b6f](https://github.com/danfma/metano/commit/5ef3b6fba5fbf427db010bacbd73c252e707dfc4))
* **compiler:** unify diagnostics sink discipline across TS + Dart ([#226](https://github.com/danfma/metano/issues/226)) ([15a9d52](https://github.com/danfma/metano/commit/15a9d52a13b2147814ef59e3d36c35b4020e5395)), closes [#218](https://github.com/danfma/metano/issues/218)
* migrate SolidJS signal binding onto JS-interop primitives ([6189a5e](https://github.com/danfma/metano/commit/6189a5e660b102d427c9f448bc72cbd76756dc37))

### 📝 Documentation

* **compiler:** refresh stale caching/annotation docstrings ([#232](https://github.com/danfma/metano/issues/232)) ([f0fc47b](https://github.com/danfma/metano/commit/f0fc47bb29a4b32334411cd21de7e11174b90b0f))
* ratify constitution and add baseline spec with multi-target evolution roadmap ([6af0d36](https://github.com/danfma/metano/commit/6af0d368e84b514bd38cbbd45fa77908ece0be4e))
* reconcile plan/tasks signal-lowering with shipped feature-003 primitives ([7bc2667](https://github.com/danfma/metano/commit/7bc266780d3d075d81f3f16b8b5416411ef547ac))
* retire old-spec after baseline parity sign-off ([2ed272f](https://github.com/danfma/metano/commit/2ed272f77b6affe6f90adadeb6cc8d2125a98fec))

## 2.3.0

_2026-05-12_


### ✨ Features

* **compiler:** --watch mode ([#18](https://github.com/danfma/metano/issues/18)) ([8fff5a7](https://github.com/danfma/metano/commit/8fff5a7e32db3acfd830f8148d114fb584fe02d9)), closes [#21](https://github.com/danfma/metano/issues/21) [#21](https://github.com/danfma/metano/issues/21) [#211](https://github.com/danfma/metano/issues/211) [#213](https://github.com/danfma/metano/issues/213) [#214](https://github.com/danfma/metano/issues/214)
* **compiler:** add [StrictUnionGuard] for shape-level union dispatch ([#154](https://github.com/danfma/metano/issues/154)) ([f09fc40](https://github.com/danfma/metano/commit/f09fc402723912a85982bfde08348850b1c925da)), closes [#88](https://github.com/danfma/metano/issues/88)
* **compiler:** broaden MS0024 trigger to non-LINQ [Queryable] callsites ([#218](https://github.com/danfma/metano/issues/218)) ([7c47a44](https://github.com/danfma/metano/commit/7c47a44ea474077bcec11af93d0abd1a89491770))
* **compiler:** emit qualified type refs in queryable expression trees ([eae3507](https://github.com/danfma/metano/commit/eae3507fffcfb2d66b48d319dc94c588544022b7)), closes [#203](https://github.com/danfma/metano/issues/203)
* **compiler:** extend queryable walker with new + nested lambda ([#206](https://github.com/danfma/metano/issues/206)) ([e633d2f](https://github.com/danfma/metano/commit/e633d2fe5b61f8b913390327bdb712b3e79daf34))
* **compiler:** fold static readonly + pure-arithmetic captures ([#208](https://github.com/danfma/metano/issues/208)) ([0a7d251](https://github.com/danfma/metano/commit/0a7d251ff60e06c0b915674b717757564743f0be))
* **compiler:** fuse adjacent LINQ stages at build time ([#207](https://github.com/danfma/metano/issues/207)) ([bf48474](https://github.com/danfma/metano/commit/bf484742bac5e70605c54a20d5be72c990b4361a))
* **compiler:** group closure hasher for PR 3b ([8b0c270](https://github.com/danfma/metano/commit/8b0c2703cbabdd7408e15b36f5f26acbab33704e))
* **compiler:** hoist pure repeated subtrees out of captured ExprTrees ([#209](https://github.com/danfma/metano/issues/209)) ([46ad6da](https://github.com/danfma/metano/commit/46ad6da689fe8a64762f97d15be4296a65276590))
* **compiler:** incremental cache MVP — whole-build short-circuit (ADR-0021) ([9eef85a](https://github.com/danfma/metano/commit/9eef85a3fa249b3f31b2c12d577d34525385c727)), closes [#21](https://github.com/danfma/metano/issues/21) [#18](https://github.com/danfma/metano/issues/18) [#18](https://github.com/danfma/metano/issues/18) [#21](https://github.com/danfma/metano/issues/21) [#211](https://github.com/danfma/metano/issues/211) [#213](https://github.com/danfma/metano/issues/213) [#18](https://github.com/danfma/metano/issues/18)
* **compiler:** lower extension indexers via item$get / item$set helpers ([#156](https://github.com/danfma/metano/issues/156)) ([7bfe7dd](https://github.com/danfma/metano/commit/7bfe7dd2a33b88a190b7786faad71a72be9b149f))
* **compiler:** lower extension property setters via $set helpers ([#156](https://github.com/danfma/metano/issues/156)) ([6768eb5](https://github.com/danfma/metano/commit/6768eb58e26bf613d8d9c101a4cd1a4f6adf8aaa))
* **compiler:** lower static extension members to module helpers ([#156](https://github.com/danfma/metano/issues/156)) ([457d2b5](https://github.com/danfma/metano/commit/457d2b537b1bfb93cb7791ec59c8016180007bb2))
* **compiler:** MS0024 hard error for explicit queryable opt-in ([#205](https://github.com/danfma/metano/issues/205)) ([95ca446](https://github.com/danfma/metano/commit/95ca446a7d2845fcf0e5cfb48d5fc2f8001aac07))
* **compiler:** parallelize TypeTransformer per file group (ADR-0020) ([b37c6d2](https://github.com/danfma/metano/commit/b37c6d2cae5336c7e6621fd3dabc0228dd91cfdb)), closes [#21](https://github.com/danfma/metano/issues/21) [#18](https://github.com/danfma/metano/issues/18) [#211](https://github.com/danfma/metano/issues/211) [#21](https://github.com/danfma/metano/issues/21) [#211](https://github.com/danfma/metano/issues/211) [#21](https://github.com/danfma/metano/issues/21) [#18](https://github.com/danfma/metano/issues/18)
* **compiler:** per-group skip integration (PR 3c) ([6dd58b6](https://github.com/danfma/metano/commit/6dd58b61788d5c9f377b01902a9ccb4c5486df83)), closes [#21](https://github.com/danfma/metano/issues/21) [#18](https://github.com/danfma/metano/issues/18) [#21](https://github.com/danfma/metano/issues/21) [#18](https://github.com/danfma/metano/issues/18) [#211](https://github.com/danfma/metano/issues/211) [#213](https://github.com/danfma/metano/issues/213) [#214](https://github.com/danfma/metano/issues/214) [#215](https://github.com/danfma/metano/issues/215) [#216](https://github.com/danfma/metano/issues/216)
* **compiler:** per-type signature hasher for PR 3b ([590a5c6](https://github.com/danfma/metano/commit/590a5c6f579e7badf9f875d35f5d734bae75cafc))
* **compiler:** type-level dependency graph backbone for [#18](https://github.com/danfma/metano/issues/18) + [#21](https://github.com/danfma/metano/issues/21) ([fcf999f](https://github.com/danfma/metano/commit/fcf999fc9fc6fe8098b1aa395451ee7172b0ee86))
* **runtime:** migrate array provider to getStages + document linq slot ([#200](https://github.com/danfma/metano/issues/200)) ([b5f0101](https://github.com/danfma/metano/commit/b5f0101f3e079a73d646d6d54ddfbc6113b62895))
* **runtime:** publish ExprTree visitor API for queryable providers ([f77fa13](https://github.com/danfma/metano/commit/f77fa1370fbfc6f45511e00dfa7b4c94c4284c53)), closes [#198](https://github.com/danfma/metano/issues/198)

### 🐛 Bug Fixes

* address PR [#204](https://github.com/danfma/metano/issues/204) review + add pre-push targets/ sync check ([6815397](https://github.com/danfma/metano/commit/681539750b60be6ee99d78b30e600f3e6bf6e8ed)), closes [C#-style](https://github.com/danfma/C/issues/-style)
* **compiler,runtime:** record equals() routes value-wrapper fields through valueEquals ([#202](https://github.com/danfma/metano/issues/202)) ([3fd0659](https://github.com/danfma/metano/commit/3fd0659275818ef2940f0a8913164b70c267404e))
* **compiler:** gate per-group cache writes on disk-touching runs ([9776c13](https://github.com/danfma/metano/commit/9776c13340dfb83253246d38cfd1cb8b72b35260)), closes [#217](https://github.com/danfma/metano/issues/217)
* **compiler:** harden per-group cache against config drift, tampering, and error runs ([669755f](https://github.com/danfma/metano/commit/669755fadb08be396895457eac2327dcd38d782b)), closes [#217](https://github.com/danfma/metano/issues/217)
* **compiler:** make parallel TypeTransformer's shared sinks actually thread-safe ([4147f83](https://github.com/danfma/metano/commit/4147f83275cacf0b5065893b3b60deca89b9dde2)), closes [#213](https://github.com/danfma/metano/issues/213)
* **compiler:** stabilise IrTypeSignatureHasher cache key (gemini review) ([e2a246c](https://github.com/danfma/metano/commit/e2a246cdedd08812d881388d4637b71311adb147)), closes [#216](https://github.com/danfma/metano/issues/216)
* **compiler:** tighten incremental cache key + harden cache reads ([47baaed](https://github.com/danfma/metano/commit/47baaed4b40b270f1838626d8d5656f9d096e420)), closes [#214](https://github.com/danfma/metano/issues/214)
* **compiler:** tighten WatchHost debounce + sync per Copilot review ([6e89b62](https://github.com/danfma/metano/commit/6e89b62dc0f8e9ff2d5939c3f0c03744ebffb1ec)), closes [#215](https://github.com/danfma/metano/issues/215)
* **compiler:** tighten WatchHost rename + error handling, unsubscribe Ctrl+C handler ([695002b](https://github.com/danfma/metano/commit/695002bd6422852cbb6c1b911f975355dfc53857)), closes [#215](https://github.com/danfma/metano/issues/215)
* **compiler:** verify WatchHost review findings already landed ([9212907](https://github.com/danfma/metano/commit/9212907fbc546da2570400d6f30908d97bc3a415))
* **dependency-graph:** address PR [#211](https://github.com/danfma/metano/issues/211) review findings ([4fce313](https://github.com/danfma/metano/commit/4fce313656af2406c06ab760e3c174df8a6ff99e))
* **reorg:** correct cref namespaces + harden pre-push gate against build races ([7b88091](https://github.com/danfma/metano/commit/7b8809110fc54f1cabe3ecb9fafb9b0cc74fec65))

### ♻️ Refactor

* **compiler:** route ExprTree member/method casing through target-aware policy ([#210](https://github.com/danfma/metano/issues/210)) ([21f9713](https://github.com/danfma/metano/commit/21f9713d13c5a7e4a297fee5ec70ca9906a55f83))
* **compiler:** split Metano.Compiler folders by concern (ADR-0019) ([a16aafb](https://github.com/danfma/metano/commit/a16aafbfff7f29f55b955810428f9f045b7836f5))
* **namespaces:** nest Metano.{Dart,TypeScript}.* under Metano.Compiler.* ([6276672](https://github.com/danfma/metano/commit/62766726fa477a903a452cfb49c7d88ee6d920b3))

### 📝 Documentation

* ADR-0023 — per-group skip foundation (PR 3b setup) ([ecb81db](https://github.com/danfma/metano/commit/ecb81dbcb239e129f32c8b1b48fee777192a663f))
* **dependency-graph:** scope statement to signature surface only ([ed921d5](https://github.com/danfma/metano/commit/ed921d56549e839adb58ee531b12ea232d884899))
* **plans:** seed reorganization brief ([208cb95](https://github.com/danfma/metano/commit/208cb951ddefa47c19267edcb43bbfe1f1a5bce2))
* **roadmap:** editorial charter so docs/roadmap stays focused ([abf809d](https://github.com/danfma/metano/commit/abf809d133be762f7e0500cb5867b2c0c5a46847))

## 2.2.0

_2026-05-07_


### ✨ Features

* **compiler,ts:** Phase A — lower LINQ chains to pipe form ([#20](https://github.com/danfma/metano/issues/20)) ([73cd8f5](https://github.com/danfma/metano/commit/73cd8f501ebbf9a6e889818bc553bc7c56d6253d)), closes [#31](https://github.com/danfma/metano/issues/31)
* **compiler,ts:** Phase B — capture lambda body as IR expression tree for IQueryable ([#31](https://github.com/danfma/metano/issues/31)) ([d18acf3](https://github.com/danfma/metano/commit/d18acf3f8313cb70836c7288bbb88e007a75ce09))
* **metano-runtime,annotations:** add ExprTree slot + [Queryable] attribute ([#20](https://github.com/danfma/metano/issues/20) → [#31](https://github.com/danfma/metano/issues/31) prep) ([15c0157](https://github.com/danfma/metano/commit/15c015783841d5c5e46bba01b1dd7f7522f4d444))
* **metano-runtime,compiler:** replace legacy LINQ with pipe runtime + groupBy + 32 overloads ([#20](https://github.com/danfma/metano/issues/20)) ([1462c04](https://github.com/danfma/metano/commit/1462c048630948fdcc095cf791c6c8c73575a17e))
* **metano-runtime:** linq-pipe operators wrap generator behind Symbol.iterator ([#20](https://github.com/danfma/metano/issues/20)) ([ed81252](https://github.com/danfma/metano/commit/ed81252c014e3fb1602b94124f2685a1e30f270e))
* **metano-runtime:** prototype pipe-based LINQ runtime ([#20](https://github.com/danfma/metano/issues/20)) ([92715a5](https://github.com/danfma/metano/commit/92715a5ec8acbaf4ad4c73ae9dd2e28bfb254b1c))
* **sample,runtime,compiler:** SQLite EF-lite provider via captured expression trees ([#198](https://github.com/danfma/metano/issues/198), [#200](https://github.com/danfma/metano/issues/200)) ([64ad51b](https://github.com/danfma/metano/commit/64ad51bd33cdec3f9efa26a897887bf0ea5e2b16))
* **sample:** demo IQueryable provider over arrays via Phase B trees ([#31](https://github.com/danfma/metano/issues/31)) ([59b3ebb](https://github.com/danfma/metano/commit/59b3ebbf638570656a587ca13c1ec25d28f0159c)), closes [#196](https://github.com/danfma/metano/issues/196)
* **sample:** log emitted SQL via optional logger callback ([86b1592](https://github.com/danfma/metano/commit/86b159220d8046801e2d3d72af42f9b0d93ffaf0))

### 🐛 Bug Fixes

* **compiler,sample:** address PR [#197](https://github.com/danfma/metano/issues/197) review findings ([d17b86d](https://github.com/danfma/metano/commit/d17b86d4527f63524b40b02ebad1f7669a513ecd))
* **compiler,ts:** address PR [#196](https://github.com/danfma/metano/issues/196) review findings ([3ae80a3](https://github.com/danfma/metano/commit/3ae80a3b5fdbbd79cbcf9985a15da53c3c964bd4))
* **metano-runtime,compiler:** address PR [#195](https://github.com/danfma/metano/issues/195) review findings ([6652f9f](https://github.com/danfma/metano/commit/6652f9fd8de530ebd1e2bd142089c097221255f9))
* **metano-runtime:** export Grouping and GroupByOp types from linq barrel ([4ed1ae5](https://github.com/danfma/metano/commit/4ed1ae5686c12e7282a59db6e74595a0fd50d53e))
* **sample,compiler:** address PR [#201](https://github.com/danfma/metano/issues/201) review findings ([a49efe8](https://github.com/danfma/metano/commit/a49efe8312c4dd04c92e63a98afeb637d00d2679))
* **sample:** biome compliance + main.ts entry for bun run . ([c0272bb](https://github.com/danfma/metano/commit/c0272bb6a992daf194f4cbbe18802bc8ed116b61)), closes [C#-emitted](https://github.com/danfma/C/issues/-emitted)

### ♻️ Refactor

* **metano-runtime:** linq-pipe operators return tagged descriptors ([#20](https://github.com/danfma/metano/issues/20)) ([47bbe7c](https://github.com/danfma/metano/commit/47bbe7c448511bef1edd5e2aaeed79a336546de2))
* **metano-runtime:** nest queryable meta + add ExprCapture ([#20](https://github.com/danfma/metano/issues/20) → [#31](https://github.com/danfma/metano/issues/31)) ([7e635eb](https://github.com/danfma/metano/commit/7e635ebf99095ef11471030ba66a0589202e560b)), closes [#5](https://github.com/danfma/metano/issues/5) [#6](https://github.com/danfma/metano/issues/6)

### 📝 Documentation

* **linq:** fix stale linq-pipe paths + expression field name in PR [#195](https://github.com/danfma/metano/issues/195) review ([f39ede1](https://github.com/danfma/metano/commit/f39ede10c73839a81f2ddde93664e195523152f1))

## 2.1.0

_2026-05-02_


### ✨ Features

* **cli:** add --dry-run flag ([#19](https://github.com/danfma/metano/issues/19)) ([d035bba](https://github.com/danfma/metano/commit/d035bba173076abb3c8b6fd26f5db80419542207))
* **ts:** support direct recursion in [Inline] methods via named function expression ([#194](https://github.com/danfma/metano/issues/194)) ([04f5813](https://github.com/danfma/metano/commit/04f5813c3ba1dcfd79e1978980399fcb69cab9c1))

### 🐛 Bug Fixes

* **compiler:** normalize args at [Emit] template call site ([#192](https://github.com/danfma/metano/issues/192)) ([221f54d](https://github.com/danfma/metano/commit/221f54dac25da209f79ccc61ac6277cce036e110))
* **compiler:** propagate [Name(target, ...)] through method/interface type references ([#170](https://github.com/danfma/metano/issues/170)) ([4cc97be](https://github.com/danfma/metano/commit/4cc97be9bd614843f3e0d78ee18529ec1a10afe4))
* **import-collector:** register [NoContainer] method-group references for import ([#179](https://github.com/danfma/metano/issues/179)) ([b88595e](https://github.com/danfma/metano/commit/b88595e81e3e2930fe6728329389a21b5417551c))
* **import-collector:** scan referenced assemblies for [NoContainer] exports ([#178](https://github.com/danfma/metano/issues/178)) ([f171da5](https://github.com/danfma/metano/commit/f171da59a9f40ff1ae5696df65fdccf5d6582ebf))
* **ts:** drop parameter-property modifiers on dispatcher overload sigs + rest impl ([#25](https://github.com/danfma/metano/issues/25)) ([d5e2f6d](https://github.com/danfma/metano/commit/d5e2f6dfbabca88324ac7fb86650123b854aa03b))
* **ts:** split record positional params into field + rest ctor parameter ([#152](https://github.com/danfma/metano/issues/152)) ([dbf3fc3](https://github.com/danfma/metano/commit/dbf3fc3bbc5ecb1c5b601e2a542102a09a5f74e4)), closes [#145](https://github.com/danfma/metano/issues/145)

### ♻️ Refactor

* **import-collector:** bundle walker buckets into ImportCollectionSink ([#192](https://github.com/danfma/metano/issues/192)) ([9a942d2](https://github.com/danfma/metano/commit/9a942d2dfc93bdcf5bab9a761439c6b2a6471fe7))

### 📝 Documentation

* **annotations:** pin [External] + [Import] resolution recipe ([#190](https://github.com/danfma/metano/issues/190)) ([285ceb2](https://github.com/danfma/metano/commit/285ceb25d955de47261909e89c6ed4dc65770bff))

## 2.0.0

_2026-05-01_


### ⚠ BREAKING CHANGES

* **annotations:** [NoEmit] and [NoTranspile] are removed. Replace any
[NoEmit] usage with [Ignore] (semantics carry over). Replace any
[NoTranspile] with [Ignore] when the type is .NET-only, or with [External]
when it is an ambient TypeScript shape that transpiled code legitimately
references. Member-level [NoTranspile] without a replacement should switch
to member-level [Ignore].

Compiler changes:

- ValidateIgnoreReferences now skips members marked [Ignore] (an [Ignore]
  helper that takes an [Ignore] marker stays silent).
- Member-level reference validation: invocations and member access on
  [Ignore] methods/properties/fields/events from transpilable code raise
  MS0013 with the member's owner.member name in the message.
- IrTypeRefMapper threads the active TargetLanguage through HasIgnore so a
  type ignored only for the active backend is correctly marked
  IsIgnored=true in the IR (and IsTranspilable=false), keeping downstream
  paths like lambda parameter lowering and runtime guard generation
  consistent with the per-target contract.
- IR field IrNamedTypeSemantics.IsNoEmit renamed to IsIgnored.
- DiagnosticCodes.NoEmitReferencedByTranspiledCode renamed to
  IgnoreReferencedByTranspiledCode (the MS0013 code itself is unchanged).

Tests:

- New IgnoreDotNetOnlyTests pin the MS0013 contract on every reference
  position (parameter, return, field type, body, generic argument,
  per-target gating).
- Former NoEmitTranspileTests renamed to ExternalAmbientTranspileTests; the
  ambient cases all reference [External] now, matching the new semantics.
- Test methods disambiguated (Ignore_ExcludesTypeFromAssemblyWideTranspile,
  Ignore_OverridesExplicitTranspileAttribute, IgnorePerTarget_*).

### ✨ Features

* **annotations:** collapse [NoEmit]/[NoTranspile]/[Ignore] into single [Ignore] ([06b5e43](https://github.com/danfma/metano/commit/06b5e438abe878819f5e5f0d85ba37a9c9d97f04))

### 🐛 Bug Fixes

* **ts:** align import-type form with Biome's expectation ([5714185](https://github.com/danfma/metano/commit/5714185cfc2bca810e2db72552ca2da322e40b08))

## 1.1.0

_2026-05-01_


### ✨ Features

* **compiler:** enhance type checking, import handling, and constructor extraction ([0ec6081](https://github.com/danfma/metano/commit/0ec60812ffbe5dfbe33dd95f78864cd6347651be))

## 1.0.3

_2026-04-30_


### 🐛 Bug Fixes

* **release:** use RELEASE_TAG instead of GITHUB_REF_NAME for npm publish ([13db217](https://github.com/danfma/metano/commit/13db2176009b9c4c115cd0a09f1eac8b3149db5b))

## 1.0.2

_2026-04-30_


### ♻️ Refactor

* **release:** drop dotnet-releaser, use dotnet pack + nuget push directly ([f6c98af](https://github.com/danfma/metano/commit/f6c98af567533b072d63ce5e1747c83a0b250f8f))

## 1.0.1

_2026-04-30_


### 🐛 Bug Fixes

* **release:** install npm deps before metano-runtime build in publish-on-tag ([feba5dc](https://github.com/danfma/metano/commit/feba5dc15fa2302d2e3c8a95783284706f6f27cb))
* **release:** split publish-on-tag into independent NuGet and npm jobs ([7b1a5d2](https://github.com/danfma/metano/commit/7b1a5d23c1468336dc6b45c67a9fd1e240e1d85b))

## 1.0.0

_2026-04-30_


### ✨ Features

* **compiler:** add --file-prefix CLI flag for opaque generated-file headers ([8c05ec1](https://github.com/danfma/metano/commit/8c05ec19d63ad0fb82ea4aae92686ce7357531ee))
* **ir:** materialize [Inline] method as lambda when passed as value ([129661d](https://github.com/danfma/metano/commit/129661de3ca3ece36eb43bce807be0369eb638a9)), closes [#193](https://github.com/danfma/metano/issues/193)

### ♻️ Refactor

* **annotations:** rename [Erasable] → [NoContainer] + add InlineMode ([358fb37](https://github.com/danfma/metano/commit/358fb37985ac827e49eba757b9efda618227f1ed))

### 📝 Documentation

* add ADR-0017 + update spec catalogs for [NoContainer] / InlineMode ([31859c6](https://github.com/danfma/metano/commit/31859c6d7acf7187f588b3d0918c7410992ca7ed))

## 0.9.0

### ✨ Features

- **Dart target prototype.** New `Metano.Compiler.Dart` project ships
  the shape-only Dart backend: `metano_runtime` Dart package with
  hashing/equality primitives, `MetanoObject` base injection,
  `DartImportCollector` wired to runtime requirements, declarative
  BCL mappings via target-aware `[MapMethod]` attributes, and Dart
  delegate lowering to typedefs.
- **`[ObjectArgs]` family.** Object-literal call shape now covers
  static methods, instance methods, and constructors via a `create`
  factory pattern (#163, #167). Type arguments survive the lowering
  (#169) and trailing `params` arguments fold into an array literal
  (#186).
- **`[Emit]` template `$T0` placeholder.** Generic type arguments
  splice into the lowered template verbatim — `Foo.Of<Bar>(...)`
  emits with `Bar` in the template body. Closes #189.
- **Method-level `[Import]` lowering.** A method annotated with
  `[Import(name, from)]` now lowers every call site to a direct
  invocation of the imported identifier and auto-emits the import
  line on the consumer file. The declaring class no longer emits a
  stub. Pairs with `[Emit]` for templated facades. Closes #188.
- **`[ImportAlias]` attribute.** File-scoped TS module carrier for
  picking the import binding name when the C# type's name collides
  (#184). Complements automatic alias synthesis when an erasable
  factory shadows a transpilable type (#183) and propagation of C#
  `using X = Y;` aliases through to TS imports (#182).
- **TypeScript class inheritance.** `extends` + `abstract` modifiers
  emit on the class surface (#118). Sealed hierarchies emit a union
  guard built around a shared discriminator (#88).
- **Extension helper lowering.** Transpilable extension members now
  lower to helper calls at the call site (#156); names propagate
  through `[Name]` and clashes between extension classes raise the
  new MS0021 diagnostic.
- **Function shape coverage.** Default parameter values emit on
  methods and functions (#115). Named arguments reorder into
  declaration order (#157). C# `params` map to TS rest parameters
  (#145). `[Transpile]` delegates emit named type aliases (#122).
- **Internal-visibility surface.** `internal` members now reach the
  TS class output instead of being silently dropped (#162).
- **`[Inline]` propagation.** A static class marked `[Inline]`
  propagates the marker to every member, removing the per-member
  bookkeeping (#107).
- **`[Erasable]` diagnostic — MS0020.** Two `[Erasable]` factories
  resolving to the same emitted name surface as a hard error
  pointing at both definitions.
- **`[NoEmit]` / `[External]` redefinition.** `[NoEmit]` becomes a
  pure .NET-only painting marker; `[External]` widens to cover every
  ambient binding shape that previously leaned on `[NoEmit]`. Class-
  level flatten dropped from `[External]` — flatten now requires
  `[Erasable]` opt-in (#106 PR1–5). MS0013 surfaces misuse.
- **DOM bindings library.** New `Metano.TypeScript.DOM` project
  exposes `Document`, `Window`, `HtmlElement`, etc. as `[External]`
  ambient classes; `Js.Document` / `Js.Window` provide `[Erasable]`
  globals shortcuts.
- **SampleCounterV3 + V4 + V5.** V3 reworked as a mini-MVU/Flutter
  DSL. V4 wires a Flutter-style widget facade through `[Erasable]`
  + `[ObjectArgs]`. V5 ships an Inferno virtual-DOM consumer end-to-
  end with a JSX-flavored widget DSL.

### 🐛 Bug Fixes

- C# 14 `extension(R r) { … }` members now emit once instead of twice
  (Roslyn surfaces them both lifted on the static class and inside
  a synthetic empty-name nested type).
- IR fixes: throw expressions lower to an IIFE with a throw statement
  (#160); `new T()` on a generic type parameter raises MS0019 (#161);
  primary-constructor parameter rewrites cover switch/argument/local-
  var positions (#158); override methods are segregated from sibling
  overload groups (#159); abstract method parameter initializers are
  dropped (#147); abstract modifier is suppressed on records (#144);
  default initializers are dropped when a constructor parameter
  default already covers the field (#164, #165).
- TS imports collected through function, tuple, and type-predicate
  types (#148).

### 🧰 Maintenance

- Sample regeneration is now part of CI: `dotnet build` followed by
  `bunx biome format --write targets/` produces the canonical sample
  output and the drift check diffs against it.
- `Metano-packages.slnx` shipped as a solution alias scoped to the
  publishable projects so the release pipeline avoids file-lock
  conflicts with sample `AfterBuild` targets.

## 0.8.1

### 🐛 Bug Fixes

- `package.json` writes are now additive: hand-curated `type`,
  `sideEffects`, `name`, `imports`, and `exports` survive every
  regeneration. The transpiler only seeds missing fields and refreshes
  its own `{types, import}` exports, leaving user-added subpaths and
  augmented conditional fields untouched (#136).
- `metano-runtime` now declares `"sideEffects": false` so consumers'
  bundlers can tree-shake unused helpers. Verified bundle drop from
  175.1 KB to 1.96 KB on a `HashCode`-only entry — 88× smaller (#137).

### 🧰 Maintenance

- `dotnet-releaser.toml` adds Conventional Commits autolabelers so
  `feat:` / `fix:` / `chore:` etc. land in the right release-note
  sections instead of bundling under "🧰 Misc" (#135).
- Release workflow grants `pull-requests: write` and `issues: write`
  so dotnet-releaser can read merged-PR data when assembling the
  changelog.
- Pinned `dotnet-releaser` 0.16.0 → 0.18.1 for the Tomlyn config
  deserialization fix.
- Switched to a static `CHANGELOG.md` driving release notes —
  works around the GitHub `/commits/{sha}/pulls` 5xx flakiness that
  blocked the v0.8.1 changelog auto-generation.

## 0.8.0

See [the v0.8.0 release notes](https://github.com/danfma/metano/releases/tag/v0.8.0)
for the prior changeset.
