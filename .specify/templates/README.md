# Templates — Spec Kit (referência)

Este diretório mantém os templates do processo **Spec-Driven Development** usados para gerar os artefatos desta SDD.

Os templates oficiais (versão completa e sempre atualizada) estão em:
- [github/spec-kit/templates](https://github.com/github/spec-kit/tree/main/templates)

## Templates usados nesta SDD

| Template | Gerou | Artefato resultante |
|----------|-------|---------------------|
| `constitution-template.md` | Constituição do projeto | `.specify/memory/constitution.md` |
| `spec-template.md` | Especificação da feature | `specs/001-ticketmaster-platform/spec.md` |
| `plan-template.md` | Plano de implementação | `specs/001-ticketmaster-platform/plan.md` |
| `tasks-template.md` | Lista de tarefas | `specs/001-ticketmaster-platform/tasks.md` |
| `checklist-template.md` | Verificação de consistência (converge) | usado na revisão de qualidade |

## Fluxo do processo

```
/speckit-constitution → constitution.md
/speckit-specify      → spec.md
/speckit-plan         → plan.md + research.md + data-model.md + contracts/ + quickstart.md
/speckit-tasks        → tasks.md
/speckit-implement    → código .NET 10
/speckit-converge     → validação de consistência
```
