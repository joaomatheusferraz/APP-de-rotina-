# Implementação do PRD v1.1

O documento recebido define o produto e a ordem das entregas. As seções 30–40 complementam e revisam as anteriores; a navegação adotada é Hoje, Rotina, Treino, Progresso e Perfil. A implementação segue fases pequenas para evitar apresentar telas vazias como recursos concluídos.

| Etapa | Escopo | Situação |
| --- | --- | --- |
| 1 | Arquitetura, SQLite, tema, navegação e testes | Implementada; validação Android em VALIDACAO.md |
| 2 | Áreas completas, hábitos, recorrência, conclusões e streaks | Hábitos e métricas implementados; faltam ocultar áreas e lembretes opcionais |
| 3 | Hoje, check-in, água e fechamento do dia | Implementada; telas verificadas no moto g84; fluxo com dados reais ainda não exercitado |
| 4 | Tarefas, compromissos, calendário e notificações | Implementados para tarefas e compromissos; lembretes locais opcionais, sujeitos ao Android; teste de entrega real do alerta pendente |
| 5 | Metas, Kanban e seed opcional Projeto 80 kg | Implementada; modelo editável sem criação automática; testes e validação Android em VALIDACAO.md |
| 6 | Peso, medidas, dashboards e gráficos | Implementada; validação Android em VALIDACAO.md |
| 7 | Treinos, séries, histórico, versionamento e periodização | Implementada; validação Android em VALIDACAO.md |
| 8 | Foco e estatísticas | Implementada; validação Android em VALIDACAO.md |
| 9 | Quadro dos Sonhos e vínculos opcionais | Implementada; validação Android em VALIDACAO.md |
| 10 | Dashboard configurável e refinamento com prints do Notion | Implementada; validação Android em VALIDACAO.md |
| 11 | Backup, restauração, exportação e privacidade | Implementada; validação automatizada concluída |
| 12 | Diário/notas rápidas e refinamento final | Implementada; validação Android em VALIDACAO.md |
| Posterior | Backend seguro, propostas de IA e widgets | Fora desta entrega |

## Decisões da fundação

- C# / MAUI Android, alinhado à sugestão do PRD e ao SDK instalado. .NET 9 é uma base de desenvolvimento local; atualização para versão suportada é requisito antes de produção.
- SQLite com driver Microsoft.Data.Sqlite. Repositório abstrato injetado na UI; domínio não conhece plataforma ou banco.
- Um perfil local, sem autenticação; nome é opcional. Temas escuro (padrão), claro e sistema persistem no banco.
- Áreas têm GUID, nome, cor, ordem e estado de arquivamento. Empates de ordem são resolvidos por nome e identificador. Ocultar sem arquivar permanece pendente.
- Banco começa vazio, sem presumir que 62 kg ainda é o peso atual. O modelo opcional Projeto 80 kg exige revisão e confirmação antes de salvar.
- Não foram implementados serviços fictícios de calendário, notificações ou IA. Seus contratos serão definidos com os casos de uso de cada etapa; futuras escritas de IA deverão passar por confirmação explícita.
- Sem integração com Sites ou app web: este é um projeto Android nativo.

## Regras de hábitos implementadas

Frequências: todos os dias, dias da semana escolhidos e X vezes por semana (1 a 7). A semana começa na segunda-feira. Para X vezes, cumprir a meta encerra as oportunidades daquela semana; o resto é descanso. A semana aberta não interrompe a sequência antes de terminar. Semana parcial por início ou revisão usa o menor valor entre meta semanal e dias disponíveis. Uma mudança no meio da semana separa segmentos de programação. Conclusões e correções são eventos separados, vinculados à versão vigente, e ficam no histórico. Pausas e intervalos arquivados não criam falhas.

O dia atual ainda aberto não quebra antecipadamente o streak. O reset visual vem da data local da ocorrência; ele não apaga registros. A tela de detalhes mostra realizados/programados em 7, 30 e 90 dias e no mês atual, sequência atual e melhor, calendário mensal e notas opcionais. Para frequência flexível, a sequência conta semanas cumpridas; para as demais, conta oportunidades programadas. O primeiro atalho + cria hábitos na tela Hoje e na aba Rotina. Lembretes, horário e os outros tipos de item serão acrescentados nas respectivas etapas.

## Check-in e fechamento do dia

O check-in usa a data local e campos opcionais para horário de acordar, duração e avaliação do sono, humor, tempo de celular, meta de água e nota. A escala de humor (3, 5 ou 10 pontos) e a meta padrão de água são configuráveis em Perfil. A meta e a escala usadas num dia registrado ficam guardadas nesse dia, mesmo quando os padrões mudam depois.

Água é registrada por eventos positivos e negativos: os botões +250/+500 ml somam, e a correção acrescenta um ajuste sem apagar os eventos anteriores. O fechamento do dia registra um resumo imutável de hábitos, água e treinos concluídos, avaliação e nota opcionais; reabrir acrescenta outro evento. Foco permanece indisponível até sua etapa. A tela Histórico dos dias permite consultar registros anteriores.

## Metas e Plano de Ação

Metas guardam título, descrição, data inicial, prazo opcional, unidade, valor inicial e alvo. O progresso numérico é acrescentado como eventos separados. Metas de peso leem pesagens guardadas em uma tabela própria, sem depender de uma meta ativa. Concluir arquiva a meta e preserva o histórico; reabrir não apaga registros. Há vínculo opcional com hábitos e tarefas. O Projeto 80 kg é apenas um modelo aberto para revisão: o valor inicial de 62 kg vem do PRD e só é salvo após confirmação do usuário; nenhuma pesagem é criada automaticamente.

O Plano de Ação usa três colunas iniciais armazenadas no banco, não fixadas nos cartões. Título, descrição, tags, área, prazo e meta vinculada são opcionais conforme o item. Cada mudança de coluna cria um evento histórico. Cartões podem ser arquivados e restaurados. Gestos de arrastar não são necessários no celular; a ação Mover cartão abre as colunas de destino.

## Peso, medidas e progresso

Pesagens e medidas têm históricos independentes. A correção desconsidera o registro antigo e acrescenta um novo, com vínculo ao anterior; o antigo continua visível. Também é possível desconsiderar um registro sem apagá-lo. Os gráficos usam apenas registros válidos no período selecionado (7, 30 ou 90 dias). A aba Progresso filtra Hábitos, Corpo, Metas e Treino e compara a consistência de hábitos com o período anterior. No calendário de hábitos, descanso/pausa, pendência do dia atual e falta têm cores diferentes. Fotos de progresso e métricas de foco aguardam suas etapas.

## Treinos e periodização

O plano de treino é composto por dias da semana e exercícios com grupo muscular, observações, séries, repetições e carga sugerida. Alterações são salvas como nova versão; sessões e séries mantêm vínculo com a versão usada. O usuário pode iniciar, retomar e finalizar uma sessão, registrar carga e repetições por série, consultar a última execução do exercício, usar um cronômetro opcional de descanso de 90 segundos e ver duração, volume e recordes. Dias de descanso, treinos perdidos, pendentes e extras são distintos no calendário de frequência.

Um ciclo de periodização contém fases editáveis com duração em semanas, séries e faixa de repetições. A sugestão 2+2+1 do PRD só aparece após escolha explícita e pode ser alterada. O ciclo pode ser pausado, retomado, reiniciado ou substituído; fases antigas permanecem associadas às sessões já realizadas. O treino programado aparece em Hoje e os concluídos entram no fechamento do dia. O app ainda não oferece notificações específicas para treino nem edição de uma série já concluída.

## Foco e estatísticas

Sessões de foco guardam título, categoria, duração alvo, início, pausas, duração real, avaliação opcional e nota. Uma sessão aberta pode ser pausada, retomada e encerrada; o banco impede duas sessões abertas ao mesmo tempo. A tela de Foco apresenta estatísticas por categoria em 7, 30 ou 90 dias e histórico das sessões encerradas. O fechamento do dia soma os minutos de foco concluídos. Categorias continuam livres, conforme os exemplos do PRD; notificações e edição de sessão encerrada ficam para uma etapa posterior.

## Quadro dos Sonhos

O mural aceita imagens escolhidas pela galeria, copiadas para uma pasta privada do aplicativo para funcionar offline. Cada cartão pode ter título, descrição, tamanho pequeno/médio/grande, meta e cartão do Plano de Ação opcionais. O cartão abre a imagem em tela cheia, pode ser movido para cima ou para baixo e removido por arquivamento sem apagar o arquivo ou o histórico do banco. A galeria só é acessada quando o usuário toca em adicionar imagem.

## Planejamento

Tarefas pontuais têm título, descrição, prazo opcional, prioridade, área, conclusão e arquivamento. Compromissos têm data, horário, duração, local/nota, área e arquivamento. Ambos podem ser editados sem perder o identificador. O calendário interno mostra quantos itens há por dia e abre a agenda; Hoje mostra os itens relevantes. Itens arquivados podem ser consultados e restaurados.

O usuário pode escolher um lembrete local por item. O Android pede permissão de notificação quando o primeiro lembrete é configurado. Alarmes anteriores são cancelados ao editar, concluir ou arquivar; após reiniciar, o app tenta reagendar os avisos. São alarmes aproximados, sujeitos ao gerenciamento de energia do sistema. A opção de calendário Android abre o formulário externo preenchido, sem ler o calendário nem criar o evento sem a ação do usuário.

## Referência visual

O tema atual traduz apenas as orientações textuais: fundo escuro, cartões com espaço, verde para ações e tipografia legível. Os prints do Notion são necessários para a etapa de fidelidade visual. Não se presume que tenham sido vistos.
