# Gabarito do teste SolverBarrasTests.PorticoPreMoldadoIgualAoOpenSees (D46). Rodar: py -3.12 portico_premoldado_opensees.py
import openseespy.opensees as ops
ops.wipe(); ops.model('basic','-ndm',3,'-ndf',6)
E,G=3000.,1250.
# nós: 1 base P1, 2 consolo P1 (z350), 3 topo P1, 4 base P2, 5 consolo P2, 6 topo P2
ops.node(1,0,0,0); ops.node(2,0,0,350); ops.node(3,0,0,400)
ops.node(4,600,0,0); ops.node(5,600,0,350); ops.node(6,600,0,400)
# apoio no consolo (lado do pilar): 7, 8 ; lado da viga: 9, 10 ; eixo da viga: 11, 12
ops.node(7,25,0,350); ops.node(8,575,0,350)
ops.node(9,25,0,350); ops.node(10,575,0,350)
ops.node(11,25,0,375); ops.node(12,575,0,375)
ops.fix(1,1,1,1,1,1,1); ops.fix(4,1,1,1,1,1,1)
ops.geomTransf('Linear',1,1,0,0)   # pilares
ops.geomTransf('Linear',2,0,0,1)   # viga
Ap,Ip,Jp=1600.,40**4/12,360533.
Av,Iyv,Izv,Jv=1000.,20*50**3/12,50*20**3/12,99805.
ops.element('elasticBeamColumn',1,1,2,Ap,E,G,Jp,Ip,Ip,1)
ops.element('elasticBeamColumn',2,2,3,Ap,E,G,Jp,Ip,Ip,1)
ops.element('elasticBeamColumn',3,4,5,Ap,E,G,Jp,Ip,Ip,1)
ops.element('elasticBeamColumn',4,5,6,Ap,E,G,Jp,Ip,Ip,1)
ops.element('elasticBeamColumn',5,11,12,Av,E,G,Jv,Iyv,Izv,2)
ops.rigidLink('beam',2,7); ops.rigidLink('beam',5,8)
ops.rigidLink('beam',11,9); ops.rigidLink('beam',12,10)
k=[50.,50.,1e5,1e7,10.,10.]
for i,kk in enumerate(k): ops.uniaxialMaterial('Elastic',10+i,kk)
ops.element('zeroLength',6,7,9,'-mat',10,11,12,13,14,15,'-dir',1,2,3,4,5,6)
ops.element('zeroLength',7,8,10,'-mat',10,11,12,13,14,15,'-dir',1,2,3,4,5,6)
ops.timeSeries('Linear',1); ops.pattern('Plain',1,1)
ops.eleLoad('-ele',5,'-type','-beamUniform',0.,-0.3)   # Wy, Wz locais
ops.load(3,5.,0,0,0,0,0); ops.load(6,0,3.,0,0,0,0)
ops.constraints('Transformation'); ops.numberer('RCM'); ops.system('UmfPack'); ops.test('NormDispIncr',1e-10,10)
ops.algorithm('Linear'); ops.integrator('LoadControl',1); ops.analysis('Static'); ops.analyze(1)
ops.reactions()
for n in (1,4): print('R',n,' '.join('%.6f'%v for v in ops.nodeReaction(n)))
for n in (3,6,11,12,9): print('U',n,' '.join('%.8e'%v for v in ops.nodeDisp(n)))
print('FV',' '.join('%.6f'%v for v in ops.eleResponse(5,'localForce')))
for e in (6,7): print('FM',e,' '.join('%.6f'%v for v in ops.eleResponse(e,'force')))
print('FP',' '.join('%.6f'%v for v in ops.eleResponse(1,'localForce')))
