# Gabarito do pórtico metálico (D48): pilares W250X32.7 de 400 cm engastados na base, viga W310X38.7 de 600 cm ligada
# rigidamente, carga uniforme de 0,1 kN/cm na viga (sem peso próprio). Unidades: cm, kN. Propriedades do PCEB (PerfilI).
# Rodar: py -3.12 portico_metalico_opensees.py  (openseespy)
import openseespy.opensees as ops

E, G = 20000.0, 7700.0
# W250X32.7 (pilar) e W310X38.7 (viga): A, Ix (forte), Iy (fraco), J calculados pelo PCEB
P = dict(A=41.88, Ix=4947.29, Iy=474.66, J=9.97)
V = dict(A=49.32, Ix=8483.53, Iy=723.08, J=12.49)
ops.wipe(); ops.model('basic', '-ndm', 3, '-ndf', 6)
ops.node(1, 0, 0, 0); ops.node(2, 0, 0, 400); ops.node(3, 600, 0, 400); ops.node(4, 600, 0, 0)
ops.fix(1, 1, 1, 1, 1, 1, 1); ops.fix(4, 1, 1, 1, 1, 1, 1)
ops.geomTransf('Linear', 1, 1, 0, 0)    # pilares: alma no plano x–z (flexão do pórtico em torno do eixo forte)
ops.geomTransf('Linear', 2, 0, 0, 1)    # viga: alma vertical
ops.element('elasticBeamColumn', 1, 1, 2, P['A'], E, G, P['J'], P['Ix'], P['Iy'], 1)
ops.element('elasticBeamColumn', 2, 2, 3, V['A'], E, G, V['J'], V['Ix'], V['Iy'], 2)
ops.element('elasticBeamColumn', 3, 4, 3, P['A'], E, G, P['J'], P['Ix'], P['Iy'], 1)
ops.timeSeries('Linear', 1); ops.pattern('Plain', 1, 1)
ops.eleLoad('-ele', 2, '-type', '-beamUniform', 0, -0.1)   # Wy = 0, Wz = −0,1 (z local da viga = vertical)
ops.system('BandGeneral'); ops.numberer('RCM'); ops.constraints('Plain'); ops.integrator('LoadControl', 1); ops.algorithm('Linear'); ops.analysis('Static')
ops.analyze(1)
f = ops.eleForce(2)
print('viga: forças na ponta I', [round(x, 3) for x in f[:6]], 'J', [round(x, 3) for x in f[6:]])
print('reação base', [round(x, 3) for x in ops.nodeReaction(1)] if ops.reactions() == 0 else '')
print('flecha no meio aprox.: desl. z do nó 2', ops.nodeDisp(2))
