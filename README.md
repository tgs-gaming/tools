# ToolsPackageManager

## Resumo
O ToolsPackageManager e uma ferramenta de Editor do Unity que centraliza a gestao de pacotes internos hospedados em repositorio Git. Com ela, voce pode descobrir, instalar, atualizar e publicar pacotes atraves de uma interface unificada, reduzindo o trabalho manual e garantindo consistencia entre as equipes de desenvolvimento.

Link Repositorio: https://github.com/tgs-gaming/tools

## Funcionalidades
- Descoberta de pacotes: identifica automaticamente pacotes disponiveis a partir de branches `tool/<id>` no repositorio.
- Leitura de metadados: extrai informacoes do `package.json` diretamente da branch do pacote (nome, descricao e versao minima do Unity).
- Listagem de versoes: exibe todas as versoes disponiveis com base nas tags do repositorio no formato `<id>-v<versao>`.
- Instalacao e configuracao: instala pacotes e inicializa automaticamente o repositorio Git local.
- Notificacao de atualizacoes: indica quando ha uma versao mais recente disponivel para pacotes instalados.
- Criacao de pacotes: gera novos pacotes com estrutura padronizada e configuracao inicial.
- Publicacao de mudancas: facilita commits e pushes de alteracoes diretamente pela interface.
- Versionamento: permite criar e publicar novas versoes atraves de tags Git.

## Limitacoes
- Revisao de codigo: nao substitui processos de Pull Request, code review ou integracao continua.
- Documentacao: nao gera documentacao tecnica automaticamente.
- Validacao de compatibilidade: verifica apenas a versao minima do Unity, sem validar compatibilidade funcional entre ferramentas.
- Resolucao de conflitos: nao resolve conflitos de Git automaticamente.

## Como acessar e disponibilidade
A ferramenta esta disponivel a partir da branch `staging/v26.1/final-release` e pode ser acessada pelo menu `TGS/Package Manager`.

## Fluxos principais

### 1) Instalar uma ferramenta existente
1. Abra o ToolsPackageManager no Unity.
2. Clique em Refresh para atualizar a lista de pacotes disponiveis.
3. Localize o pacote desejado na lista.
4. Escolha uma das opcoes:
   - Clique em Install Latest para instalar a versao mais recente.
   - Ou selecione uma versao especifica no dropdown e clique em Install Selected Version.
5. Aguarde a conclusao da instalacao.

Resultado esperado: o pacote e adicionado ao projeto e o repositorio Git local e automaticamente inicializado na pasta do pacote.

### 2) Atualizar uma ferramenta instalada
1. Localize o pacote instalado na lista.
2. Caso haja uma versao mais recente disponivel, o aviso "Update available" sera exibido.
3. Escolha uma das opcoes:
   - Clique em Install Latest para atualizar para a versao mais recente.
   - Ou selecione uma versao especifica no dropdown e clique em Install Selected Version.

Resultado esperado: a versao anterior do pacote e substituida pela nova versao selecionada.

### 3) Criar uma nova ferramenta
1. Clique em Create Package.
2. Preencha os campos obrigatorios:
   - Nome do Pacote
   - Autor
   - Descricao (opcional)
   - Versao (padrao: 1.0.0)
3. Confirme a criacao.

Resultado esperado: um novo pacote e criado na pasta `packages/<id>` com a seguinte estrutura:

```
packages/<id>/
  package.json
  README.md
  CHANGELOG.md
  Editor/
    <id>.editor.asmdef
  Runtime/
    <id>.asmdef
```

Nesse momento, o pacote estara apenas LOCAL, sendo necessario realizar o PUSH do mesmo para o repositorio remoto. Faca isso pelo botao Publish.

Observacoes importantes:
- O pacote inclui dois Assembly Definitions (`.asmdef`): um para scripts de runtime e outro para scripts de editor.
- Mantenha o arquivo `package.json` sempre atualizado com informacoes corretas de nome, versao, descricao e autor.
- Mantenha o arquivo `README.md` sempre atualizado, pois ele sera exibido como documentacao principal no GitHub.
- Caso o PUSH retorne algum erro de permissoes, garanta que:
  - Voce tem acesso ao repositorio: https://github.com/tgs-gaming/tools
  - Seu github-token esta adicionado nas configuracoes da ferramenta

### 4) Publicar uma nova versao
Processo de publicacao:
1. Realize as alteracoes necessarias nos arquivos do pacote.
2. Quando houver arquivos modificados, o botao Commit sera habilitado.
3. Clique em Commit, revise a lista de arquivos alterados e insira uma mensagem descritiva.
4. Apos o commit, o botao Push Update ficara disponivel.
5. Clique em Push Update para enviar as alteracoes ao repositorio remoto.
6. Para publicar uma nova versao oficial:
   - Clique em Create Version
   - Digite o numero da versao (exemplo: `1.5.0`)
   - Confirme a criacao
7. Uma tag Git no formato `<id>-v<versao>` e criada e enviada automaticamente ao repositorio.

Resultado esperado: as alteracoes e a nova versao ficam disponiveis para toda a equipe atraves do repositorio compartilhado.

## Observacoes gerais
- Quando um pacote nao possui tags de versao, a interface exibe a versao definida no arquivo `package.json`.
- Para repositorios locais, os botoes de controle Git (Commit, Push, Create Version) sao exibidos diretamente na interface do pacote.
- Para repositorios remotos, a interface prioriza as funcionalidades de instalacao e atualizacao.

