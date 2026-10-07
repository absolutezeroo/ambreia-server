-- Crée l'utilisateur MySQL en lecture seule de `vortex-imager`.
--
-- Pourquoi c'est nécessaire : l'imager est un service séparé, joignable depuis le réseau Docker, et
-- il ne fait que LIRE — vérifié table par table dans `packages/vortex-imager/src`, sept `FROM`/`JOIN` (cinq tables depuis la coupe au noyau)
-- et aucun `INSERT`/`UPDATE`/`DELETE`. Lui donner le compte de l'émulateur lui donnerait le droit
-- d'écrire partout, dont `players` et `furniture`, pour dessiner des avatars. Un compte dédié rend
-- vrai ce qui n'était que supposé : même si le processus est compromis ou bogué, il ne peut rien
-- modifier.
--
-- Ce qu'il lit, et rien d'autre :
--   players               la figure derrière `?user=`
--   rooms                 le rendu d'appartement
--   room_models           le plan de la salle rendue
--   furniture             les meubles qui s'y trouvent
--   furniture_definitions ce que chacun est
--
-- Avant de lancer : remplacer <MOT_DE_PASSE> par une vraie valeur, et `development` par le nom de
-- la base si ce n'est pas le sien. Le mot de passe ne doit PAS être commité ici — il va dans les
-- variables Coolify de l'app imager (`IMAGER_DB_PASSWORD`).
--
-- Idempotent : relançable sans erreur. Relancer ne change pas le mot de passe d'un compte existant
-- (voir l'ALTER USER commenté en bas).

-- `'%'` et non une IP : l'imager appelle depuis un conteneur du réseau Docker, dont l'adresse change
-- à chaque recréation. La base n'est pas exposée publiquement, et ce compte ne peut de toute façon
-- que lire cinq tables. Pour resserrer davantage, remplacer par le sous-réseau réel du serveur
-- (`'172.16.%'` sur l'installation Coolify qui a servi de référence).
CREATE USER IF NOT EXISTS 'vortex_imager'@'%' IDENTIFIED BY '<MOT_DE_PASSE>';

GRANT SELECT ON `development`.`players`               TO 'vortex_imager'@'%';
GRANT SELECT ON `development`.`rooms`                 TO 'vortex_imager'@'%';
GRANT SELECT ON `development`.`room_models`           TO 'vortex_imager'@'%';
GRANT SELECT ON `development`.`furniture`             TO 'vortex_imager'@'%';
GRANT SELECT ON `development`.`furniture_definitions` TO 'vortex_imager'@'%';

-- Vérification. La liste doit tenir en cinq `GRANT SELECT` plus le `GRANT USAGE` que MySQL ajoute
-- toujours : rien sur `*.*`, aucun `ALL PRIVILEGES`, aucun droit d'écriture.
SHOW GRANTS FOR 'vortex_imager'@'%';

-- Pour changer le mot de passe d'un compte déjà créé, décommenter :
-- ALTER USER 'vortex_imager'@'%' IDENTIFIED BY '<MOT_DE_PASSE>';
