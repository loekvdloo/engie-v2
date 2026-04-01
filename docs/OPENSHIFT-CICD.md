# OpenShift CI/CD met GitHub Actions

Deze repo bevat nu twee pipelines:

- `.github/workflows/ci.yml`
  - Draait op elke push en pull request.
  - Buildt alle services.
  - Draait alle testprojecten in `tests/*/*.csproj`.
  - Publiceert TRX + coverage artifacts.

- `.github/workflows/deploy-openshift.yml`
  - Draait automatisch op `main` en handmatig via `workflow_dispatch`.
  - Voert eerst test-gate uit op alle testprojecten.
  - Logt in op OpenShift.
  - Applyt build/runtime manifests.
  - Start OpenShift builds.
  - Herstart deployments en wacht op rollout.
  - Doet health smoke test op EventHandler route.

## Vereiste GitHub Secrets

Configureer in je repo Settings -> Secrets and variables -> Actions:

- `OPENSHIFT_SERVER`: API URL van je cluster, bv. `https://api.cluster.example.com:6443`
- `OPENSHIFT_TOKEN`: service-account token met rechten voor builds/deploys
- `OPENSHIFT_NAMESPACE`: namespace/project, bv. `engie-mca`

## OpenShift rechten

De gebruiker/token moet minimaal kunnen:

- `get/list/watch/create/update/patch` op builds, buildconfigs, imagestreams
- `get/list/watch/create/update/patch` op deployments, services, routes, configmaps
- `get` op pods/logs voor troubleshooting

## Eerste keer opzetten

1. Controleer dat `openshift/buildconfigs.yaml` nog `REPLACE_WITH_YOUR_GIT_URL` bevat.
2. De pipeline vervangt dit automatisch met de GitHub repo URL.
3. Push naar `main` of start handmatig de workflow `Deploy OpenShift`.

## Handmatig deployen vanuit GitHub

1. Ga naar Actions -> `Deploy OpenShift`
2. Klik `Run workflow`
3. Kies optioneel een `deploy_ref` (branch/tag/sha)
4. Run

## Bekende valkuilen

- Private GitHub repo + OpenShift BuildConfig Git source: als OpenShift je repo niet kan clonen, configureer een source secret in BuildConfig.
- Self-signed cluster cert: workflow gebruikt `insecure_skip_tls_verify: true`.
  - Voor productie liever trust-chain goed zetten en deze optie uit.
- Route gebruikt nu HTTP voor smoke test.
  - Als je TLS forceert, pas smoke test aan naar `https://`.
