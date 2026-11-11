'use strict';
const express=require('express');
function createGameRouter(game,authenticate){const r=express.Router();r.use(authenticate);const route=fn=>(req,res,next)=>Promise.resolve(fn(req,res)).catch(next);
r.use('/day-sessions/:id/batches',express.json({limit:'256kb'}));r.use(express.json({limit:'16kb'}));
r.get('/bootstrap',route(async(q,s)=>s.json(await game.bootstrap(q.user.id))));
r.get('/configs',route(async(q,s)=>s.json({rules:game.rules})));
r.get('/catalog',route(async(q,s)=>s.json(game.catalog())));
r.get('/player',route(async(q,s)=>s.json(await game.player(q.user.id))));
r.patch('/player/cafe',route(async(q,s)=>s.json(await game.rename(q.user.id,q.body||{}))));
r.post('/player/actions',route(async(q,s)=>s.json(await game.action(q.user.id,q.body||{}))));
r.post('/shop/purchases',route(async(q,s)=>s.json(await game.purchase(q.user.id,q.body||{}))));
r.post('/day-sessions',route(async(q,s)=>s.status(201).json(await game.start(q.user.id,q.body||{}))));
r.get('/day-sessions/:id',route(async(q,s)=>s.json(await game.status(q.user.id,q.params.id))));
r.get('/day-sessions/:id/commands',route(async(q,s)=>s.json(await game.commands(q.user.id,q.params.id,Number(q.query.afterSeq||0),Number(q.query.limit||512)))));
r.put('/day-sessions/:id/batches/:n',route(async(q,s)=>s.json(await game.batch(q.user.id,q.params.id,Number(q.params.n),q.body||{}))));
r.post('/day-sessions/:id/complete',route(async(q,s)=>s.json(await game.complete(q.user.id,q.params.id,q.body||{}))));
r.post('/day-sessions/:id/cancel',route(async(q,s)=>s.json(await game.cancel(q.user.id,q.params.id,q.body||{}))));
r.get('/operations/:id',route(async(q,s)=>s.json(await game.operationStatus(q.user.id,q.params.id))));return r;}
module.exports={createGameRouter};
